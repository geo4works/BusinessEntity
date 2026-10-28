#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BlazorServerWebLogger/BlazorServerWebLogger.csproj", "BlazorServerWebLogger/"]
COPY ["BusinessEntity/BusinessEntity.csproj", "BusinessEntity/"]
RUN dotnet restore "./BlazorServerWebLogger/BlazorServerWebLogger.csproj"
COPY . .
WORKDIR "/src/BlazorServerWebLogger"
RUN dotnet build "./BlazorServerWebLogger.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BlazorServerWebLogger.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV IS_DOCKER=true

ENTRYPOINT ["dotnet", "BlazorServerWebLogger.dll"]
