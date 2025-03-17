FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/websocket-rpc-net/websocket-rpc-net.csproj ./src/websocket-rpc-net/websocket-rpc-net.csproj
COPY src/websocket-rpc-net-generator/websocket-rpc-net-generator.csproj ./src/websocket-rpc-net-generator/websocket-rpc-net-generator.csproj
COPY test/load-test/load-test-server/load-test-server.csproj ./test/load-test/load-test-server/load-test-server.csproj
COPY test/load-test/load-test-client/load-test-client.csproj ./test/load-test/load-test-client/load-test-client.csproj
RUN dotnet restore test/load-test/load-test-client/load-test-client.csproj
COPY src/websocket-rpc-net/*.cs ./src/websocket-rpc-net/
COPY src/websocket-rpc-net/Internal ./src/websocket-rpc-net/Internal
COPY src/websocket-rpc-net/Serialization ./src/websocket-rpc-net/Serialization
COPY src/websocket-rpc-net/Testing ./src/websocket-rpc-net/Testing
COPY src/websocket-rpc-net-generator/*.cs ./src/websocket-rpc-net-generator/
COPY src/websocket-rpc-net-generator/Models ./src/websocket-rpc-net-generator/Models
COPY src/websocket-rpc-net-generator/Generator ./src/websocket-rpc-net-generator/Generator
COPY test/load-test/load-test-server/*.cs ./test/load-test/load-test-server/
COPY test/load-test/load-test-client/*.cs ./test/load-test/load-test-client/
RUN dotnet publish test/load-test/load-test-client/load-test-client.csproj --no-restore -f net9.0 -c Release -o /build

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /build .
ENTRYPOINT ["dotnet", "load-test-client.dll"]