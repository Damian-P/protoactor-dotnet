dotnet publish -c Release -r linux-musl-x64 Node1/Node1.csproj
dotnet publish -c Release -r linux-musl-x64 Node2/Node2.csproj
docker-compose down
docker system prune -f
docker-compose up --build --scale node2=1