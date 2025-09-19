# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy entire repo so central props (Directory.Build.props/Packages.props, etc.)
# are present before restore (prevents NETSDK1013).
COPY . .

# Restore & publish API (brings in all referenced projects)
RUN dotnet restore Howazit.Responses.Api/Howazit.Responses.Api.csproj
RUN dotnet publish Howazit.Responses.Api/Howazit.Responses.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN mkdir -p /app/data
EXPOSE 8080
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Howazit.Responses.Api.dll"]
