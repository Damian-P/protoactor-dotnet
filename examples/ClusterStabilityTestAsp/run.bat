dotnet publish -c Release -r linux-musl-x64 Worker/Worker.csproj
dotnet publish -c Release -r linux-musl-x64 Client/Client.csproj
docker-compose up --build