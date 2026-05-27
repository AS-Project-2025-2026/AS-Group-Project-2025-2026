# Build stage — only the worker project is compiled here.
# The build context is the repository root (docker-compose sets context: .)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src
COPY src/Integration/Nop.IntegrationWorker/ Integration/Nop.IntegrationWorker/

WORKDIR /src/Integration/Nop.IntegrationWorker
RUN dotnet publish Nop.IntegrationWorker.csproj -c Release -o /app/published --nologo

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime

# ICU is required by Microsoft.Data.SqlClient on Alpine
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app/published .

ENTRYPOINT ["dotnet", "Nop.IntegrationWorker.dll"]
