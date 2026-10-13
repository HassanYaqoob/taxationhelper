# Build from the backend folder:  docker build -f src/Kira.Api/Dockerfile -t kira-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Kira.sln ./
COPY src/Kira.Domain/Kira.Domain.csproj src/Kira.Domain/
COPY src/Kira.Application/Kira.Application.csproj src/Kira.Application/
COPY src/Kira.Infrastructure/Kira.Infrastructure.csproj src/Kira.Infrastructure/
COPY src/Kira.Api/Kira.Api.csproj src/Kira.Api/
RUN dotnet restore src/Kira.Api/Kira.Api.csproj
COPY src/ src/
RUN dotnet publish src/Kira.Api/Kira.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kira.Api.dll"]
