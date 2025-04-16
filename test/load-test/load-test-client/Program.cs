using Microsoft.Extensions.Options;
using Nickogl.WebSockets.Rpc.LoadTest.Client;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TestState>();
builder.Services.AddOptions<TestOptions>().Bind(builder.Configuration).ValidateDataAnnotations();
builder.Services.AddHostedService<TestExecutor>();

var app = builder.Build();
var options = app.Services.GetRequiredService<IOptions<TestOptions>>().Value;
var state = app.Services.GetRequiredService<TestState>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
app.UseRouting();
app.MapGet("/start", httpContext =>
{
	_ = RunTest(state, options, logger, cancellationToken: state.Start()).ContinueWith(task =>
	{
		if (task.IsFaulted)
		{
			logger.LogError(task.Exception, "Unexpected error during test execution");
		}
	});
	return Task.CompletedTask;
});
app.MapGet("/stop", async httpContext =>
{
	state.Stop();
	try
	{
		var result = await state.WaitResult().WaitAsync(timeout: TimeSpan.FromMinutes(10), httpContext.RequestAborted);
		await httpContext.Response.WriteAsJsonAsync(result);
	}
	catch (TimeoutException e)
	{
		httpContext.Response.StatusCode = StatusCodes.Status408RequestTimeout;
		await httpContext.Response.WriteAsync(e.ToString());
	}
});
app.Run();

static async Task RunTest(TestState state, TestOptions options, ILogger logger, CancellationToken cancellationToken)
{
	logger.LogInformation("Connecting clients...");
	var clients = await TestServerTestClientCollection.CreateAsync(
		new Uri(options.TestServerUrl!),
		count: options.Connections,
		options.MaximumConcurrentConnects,
		cancellationToken);

	logger.LogInformation("Running load test...");
	var errors = new ConcurrentQueue<string>();
	int messagesSent = 0;
	int messagesFailed = 0;
	var tasks = new List<Task>();
	foreach (var batch in clients.Chunk(clients.Count / Environment.ProcessorCount))
	{
		async Task Run()
		{
			await Task.Yield();

			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					foreach (var client in batch)
					{
						if (client.Aborted)
						{
							continue;
						}

						try
						{
							var (x, y, z) = (Random.Shared.Next(), Random.Shared.Next(), Random.Shared.Next());
							await client.SetPosition(x, y, z);
							Interlocked.Increment(ref messagesSent);
						}
						catch (Exception e)
						{
							errors.Enqueue(e.Message);
							Interlocked.Increment(ref messagesFailed);
							client.Aborted = true;
						}
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
		tasks.Add(Run());
	}

	var started = Stopwatch.GetTimestamp();
	await Task.WhenAll(tasks);
	var duration = Stopwatch.GetElapsedTime(started);

	logger.LogInformation("Publishing load test result...");
	state.SetResult(new TestResult()
	{
		Duration = duration,
		MessagesSent = messagesSent,
		MessagesFailed = messagesFailed,
		MessagesReceived = clients.Sum(client => client.MessagesReceived),
		Errors = [.. errors],
	});

	logger.LogInformation("Disconnecting clients...");
	await Task.WhenAny(
		clients.DisposeAsync().AsTask(),
		Task.Delay(options.MaximumRampdownDuration, CancellationToken.None));
}

internal sealed class TestExecutor(IOptions<TestOptions> options, ILogger<TestExecutor> logger, IHostApplicationLifetime lifetime) : BackgroundService
{
	private readonly TestOptions _options = options.Value;
	private readonly ILogger<TestExecutor> _logger = logger;
	private readonly IHostApplicationLifetime _lifetime = lifetime;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_options.Nodes.Count == 0)
		{
			return;
		}

		try
		{
			await Task.WhenAll(_options.Nodes.Select(async node =>
			{
				using var httpClient = new HttpClient();
				await httpClient.GetAsync($"{node.BaseUrl}/start");
			}));
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to start load test");
			_lifetime.StopApplication();
			return;
		}

		try
		{
			_logger.LogInformation("Load test started, waiting for completion...");
			await Task.Delay(_options.TestDuration, stoppingToken);
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("Load test was cancelled");
			return;
		}

		try
		{
			_logger.LogInformation("Fetching results from nodes...");
			var results = await Task.WhenAll(_options.Nodes.Select(async node =>
			{
				using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
				var response = await httpClient.GetAsync($"{node.BaseUrl}/stop");
				try
				{
					return await response.Content.ReadFromJsonAsync<TestResult>();
				}
				catch (JsonException)
				{
					throw new ApplicationException($"Unable to get test results from node '{node.BaseUrl}'. Response: {await response.Content.ReadAsStringAsync()}");
				}
			}));
			var aggregatedResult = results.Where(r => r != null).Aggregate((a, b) =>
			{
				a!.MessagesSent += b!.MessagesSent;
				a.MessagesFailed += b.MessagesFailed;
				a.MessagesReceived += b.MessagesReceived;
				a.Errors.AddRange(b.Errors);
				a.Duration = b.Duration > a.Duration ? b.Duration : a.Duration;
				return a;
			})!;
			_logger.LogInformation("Server receive throughput: {count:N2} messages/s", aggregatedResult.MessagesSent / aggregatedResult.Duration.TotalSeconds);
			_logger.LogInformation("Server send throughput: {count:N2} messages/s", aggregatedResult.MessagesReceived / aggregatedResult.Duration.TotalSeconds);
			_logger.LogInformation("Failed messages: {count}", aggregatedResult.MessagesFailed);

			var testResultDir = Directory.CreateDirectory("TestResults");
			var testResultFile = Path.Combine(Path.GetFullPath(testResultDir.ToString()), $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
			await File.WriteAllTextAsync(testResultFile, JsonSerializer.Serialize(aggregatedResult), default);
			_logger.LogInformation("Detailed results: {path}", testResultFile);

			_logger.LogInformation("Waiting for application to ramp down...");
			await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to collect test results");
		}

		_lifetime.StopApplication();
	}
}

internal sealed class TestResult
{
	public int MessagesSent { get; set; }
	public int MessagesFailed { get; set; }
	public int MessagesReceived { get; set; }
	public List<string> Errors { get; set; } = [];
	public TimeSpan Duration { get; set; }
}

internal sealed class TestState(IHostApplicationLifetime lifetime) : IDisposable
{
	private readonly object _lock = new();
	private readonly IHostApplicationLifetime _lifetime = lifetime;
	private CancellationTokenSource? _cts;
	private TaskCompletionSource<TestResult>? _resultAvailable;

	public void Dispose()
	{
		_cts?.Dispose();
	}

	public CancellationToken Start()
	{
		lock (_lock)
		{
			if (_cts != null)
			{
				throw new InvalidOperationException("Test is already running");
			}

			_resultAvailable = null;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
			return _cts.Token;
		}
	}

	public void Stop()
	{
		lock (_lock)
		{
			if (_cts == null)
			{
				throw new InvalidOperationException("Test is not running");
			}

			_cts.Cancel();
			_cts.Dispose();
			_cts = null;
		}
	}

	public void SetResult(TestResult result)
	{
		lock (_lock)
		{
			_resultAvailable ??= new();
		}
		_resultAvailable.TrySetResult(result);
	}

	public Task<TestResult> WaitResult()
	{
		lock (_lock)
		{
			return (_resultAvailable ??= new()).Task;
		}
	}
}
