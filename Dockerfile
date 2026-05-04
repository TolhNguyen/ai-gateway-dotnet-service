FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/AiGateway.Api/AiGateway.Api.csproj ./AiGateway.Api/
RUN dotnet restore ./AiGateway.Api/AiGateway.Api.csproj
COPY src/AiGateway.Api ./AiGateway.Api
RUN dotnet publish ./AiGateway.Api/AiGateway.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AiGateway.Api.dll"]
