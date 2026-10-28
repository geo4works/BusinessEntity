# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BusinessEntity/BusinessEntity.csproj", "BusinessEntity/"]
RUN --mount=type=cache,id=businessentity-nuget,target=/root/.nuget/packages,sharing=locked dotnet restore "./BusinessEntity/BusinessEntity.csproj"
COPY . .
WORKDIR "/src/BusinessEntity"
RUN --mount=type=cache,id=businessentity-nuget,target=/root/.nuget/packages,sharing=locked dotnet build "./BusinessEntity.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=businessentity-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish "./BusinessEntity.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BusinessEntity.dll"]
