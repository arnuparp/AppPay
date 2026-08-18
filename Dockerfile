# Stage 1: runtime base image (what the final container actually ships)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# The Tesseract NuGet package only ships native binaries for Windows (x64/leptonica-1.82.0.dll,
# x64/tesseract50.dll). On Linux it dlopen()s "libleptonica-1.82.0.so" / "libtesseract50.so" by
# those exact names, which Debian's tesseract-ocr package does not provide — so we install the
# real libs via apt and symlink them to the names the wrapper expects.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr \
    && LEPT_SO=$(ldconfig -p | grep -oE '/[^ ]*liblept\.so[^ ]*' | head -n1) \
    && TESS_SO=$(ldconfig -p | grep -oE '/[^ ]*libtesseract\.so[^ ]*' | head -n1) \
    && ln -sf "$LEPT_SO" /usr/lib/x86_64-linux-gnu/libleptonica-1.82.0.so \
    && ln -sf "$TESS_SO" /usr/lib/x86_64-linux-gnu/libtesseract50.so \
    && ldconfig \
    && rm -rf /var/lib/apt/lists/*

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
