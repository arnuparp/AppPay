# Stage 1: runtime base image (what the final container actually ships)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Stage 2: SDK image used only to build the app (not shipped)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Apppay/Apppay.csproj", "Apppay/"]
RUN dotnet restore "Apppay/Apppay.csproj"
COPY . .
WORKDIR "/src/Apppay"
RUN dotnet build "Apppay.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Stage 3: publish the app (trimmed output, no build tools)
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Apppay.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 4: final image — only the published output + ASP.NET runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Apppay.dll"]
