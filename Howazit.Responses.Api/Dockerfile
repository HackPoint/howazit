FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Howazit.Responses.Api/Howazit.Responses.Api.csproj", "Howazit.Responses.Api/"]
COPY ["Howazit.Responses.Application/Howazit.Responses.Application.csproj", "Howazit.Responses.Application/"]
COPY ["Howazit.Responses.Domain/Howazit.Responses.Domain.csproj", "Howazit.Responses.Domain/"]
COPY ["Howazit.Responses.Infrastructure/Howazit.Responses.Infrastructure.csproj", "Howazit.Responses.Infrastructure/"]
RUN dotnet restore "Howazit.Responses.Api/Howazit.Responses.Api.csproj"
COPY . .
WORKDIR "/src/Howazit.Responses.Api"
RUN dotnet build "./Howazit.Responses.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Howazit.Responses.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Howazit.Responses.Api.dll"]
