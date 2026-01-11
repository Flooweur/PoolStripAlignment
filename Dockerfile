# ============================================================================
# Pool Test Strip Processor - Dockerfile
# ============================================================================
# Multi-stage build for optimal image size and security
# 
# Stage 1 (build): Compiles the .NET application
# Stage 2 (runtime): Contains only the runtime and compiled application
# ============================================================================

# ---------------------------------------------------------------------------
# Stage 1: Build Stage
# ---------------------------------------------------------------------------
# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set working directory for build
WORKDIR /source

# Copy project file first for better Docker layer caching
# This means NuGet restore is cached if project file doesn't change
COPY src/PoolStripProcessor.csproj ./src/

# Restore NuGet packages
RUN dotnet restore src/PoolStripProcessor.csproj

# Copy the rest of the source code
COPY src/ ./src/

# Build the application in Release mode
RUN dotnet publish src/PoolStripProcessor.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------------------------------------------------------------------------
# Stage 2: Runtime Stage
# ---------------------------------------------------------------------------
# Use the smaller ASP.NET runtime image (not the full SDK)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install dependencies required by OpenCvSharp4.runtime.linux-x64
# The OpenCvSharp NuGet package bundles its own native OpenCV libraries,
# but they depend on system libraries for image codecs, threading, etc.
RUN apt-get update && apt-get install -y --no-install-recommends \
    # For health check
    curl \
    # Image codec libraries
    libpng16-16 \
    libjpeg62-turbo \
    libtiff6 \
    libwebp7 \
    # Video/media libraries (required by OpenCV)
    libavcodec60 \
    libavformat60 \
    libswscale7 \
    # Threading
    libgomp1 \
    # Tesseract OCR (required by OpenCvSharp even if not used)
    libtesseract5 \
    libleptonica-dev \
    # General dependencies
    libgdiplus \
    && rm -rf /var/lib/apt/lists/* \
    # Create symlinks for library version compatibility
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /usr/lib/x86_64-linux-gnu/libtesseract.so.4 || true

# Set working directory
WORKDIR /app

# Create a non-root user for security best practices
# Running as non-root reduces the impact of potential container escapes
RUN adduser --disabled-password --gecos '' --uid 1001 appuser

# Copy the published application from build stage
COPY --from=build /app/publish .

# Change ownership to the non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose the port the app runs on
# ASP.NET Core 8 uses port 8080 by default in containers
EXPOSE 8080

# Set environment variables
# - ASPNETCORE_URLS: Bind to all interfaces on port 8080
# - DOTNET_RUNNING_IN_CONTAINER: Optimize for container environment
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production

# Health check - Docker/orchestrators can use this to verify container health
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "PoolStripProcessor.dll"]