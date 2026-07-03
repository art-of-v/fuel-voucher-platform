FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy only the src project files needed for the API (no test projects)
COPY src/FuelFlow.API/FuelFlow.API.csproj src/FuelFlow.API/

# Restore with explicit RID based on container architecture so correct native binary is included
RUN UNAME_M=$(uname -m) && \
    if [ "$UNAME_M" = "aarch64" ] || [ "$UNAME_M" = "arm64" ]; then RID="linux-arm64"; else RID="linux-x64"; fi && \
    echo "Restoring for RID: $RID" && \
    dotnet restore src/FuelFlow.API/FuelFlow.API.csproj -r $RID

# Copy everything else and publish
COPY . .

# Publish with correct RID. Docnet.Core ships pdfium.so which ends up at the publish root.
RUN UNAME_M=$(uname -m) && \
    if [ "$UNAME_M" = "aarch64" ] || [ "$UNAME_M" = "arm64" ]; then RID="linux-arm64"; else RID="linux-x64"; fi && \
    echo "Publishing for RID: $RID" && \
    dotnet publish src/FuelFlow.API/FuelFlow.API.csproj \
    -c Release \
    -r $RID \
    --self-contained false \
    -o /app/out

# Diagnostic: show native binaries in publish output (visible in docker build logs)
RUN echo "=== Native binaries in publish output ===" && \
    find /app/out -name "*.so" -o -name "pdfium*" | sort

# Verify Docnet.Core's pdfium native binary was published (fails build early if missing)
RUN test -f /app/out/pdfium.so \
    || (echo "ERROR: pdfium.so not found in publish output! Listing /app/out:" && ls -la /app/out/ && exit 1)

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install native libraries required by PDFium on Debian/Linux
RUN apt-get update && apt-get install -y \
    libglib2.0-0 \
    libfontconfig1 \
    libfreetype6 \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/out .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FuelFlow.API.dll"]
