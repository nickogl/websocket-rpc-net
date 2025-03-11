# Sample App

Run a simple chat client in the browser.

## Steps

1. Launch the sample app with the appropriate VSCode launch target or with `dotnet run --project sample-app -f net8.0 -c Release`
2. Generate the browser sources by running `generate-client.sh`
3. Serve the browser sources by running `serve-client.sh`
4. Navigate to http://localhost:8080 in a browser of your choice
5. Connect to the server at http://localhost:5000 and post some messages
6. Repeat the same for multiple tabs, all clients should be synced
