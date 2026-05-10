# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

# Install Native AOT dependencies for Alpine
RUN apk add --no-cache clang build-base zlib-dev zlib-static ca-certificates tzdata

# Create an unprivileged user to run the application
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

# Optimize Docker cache by restoring NuGet packages first
COPY AOTel.slnx Directory.Build.props ./
COPY src/AOTel.Core/AOTel.Core.csproj src/AOTel.Core/
COPY src/AOTel.Hosting/AOTel.Hosting.csproj src/AOTel.Hosting/
RUN dotnet restore src/AOTel.Hosting/AOTel.Hosting.csproj -r linux-musl-x64

# Copy the rest of the source code
COPY src/ ./src/

# Publish the AOTel.Hosting project as a Native AOT self-contained binary
RUN dotnet publish src/AOTel.Hosting/AOTel.Hosting.csproj \
    -c Release \
    -r linux-musl-x64 \
    --self-contained true \
    /p:PublishAot=true \
    /p:OptimizationPreference=Size \
    /p:StaticExecutable=true \
    -o /app/publish

# Final Runtime Stage
FROM scratch AS final
WORKDIR /app
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/
COPY --from=build /usr/share/zoneinfo /usr/share/zoneinfo
COPY --from=build /etc/passwd /etc/passwd
COPY --from=build /app/publish/AOTel.Hosting .

# Set the non-root user for security
USER appuser

# Expose the OTLP HTTP ingestion port
EXPOSE 4318

# Execute the native binary directly (no .NET runtime required)
ENTRYPOINT ["./AOTel.Hosting"]
