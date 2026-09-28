# Multi-Supplier Order Router
#
#   Run the service:  docker build -t order-router .
#                     docker run --rm -p 8080:8080 order-router
#                     then open http://localhost:8080/swagger
#
#   Run the tests:    docker build --target test -t order-router-tests .
#                     docker run --rm order-router-tests

# ---- Build: restore and compile the whole solution ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the project files, so the NuGet layer is cached between code changes.
COPY OrderRouter.sln ./
COPY src/OrderRouter.Core/OrderRouter.Core.csproj src/OrderRouter.Core/
COPY src/OrderRouter.Api/OrderRouter.Api.csproj src/OrderRouter.Api/
COPY tests/OrderRouter.Tests/OrderRouter.Tests.csproj tests/OrderRouter.Tests/
RUN dotnet restore OrderRouter.sln

# Source code plus the reference data (suppliers.csv, products.csv, sample_orders.json).
COPY . .
RUN dotnet build OrderRouter.sln -c Release --no-restore

# ---- Test: run the full test suite (not part of the runtime image) ----
FROM build AS test
ENTRYPOINT ["dotnet", "test", "OrderRouter.sln", "-c", "Release", "--no-build", "--logger", "console;verbosity=normal"]

# ---- Publish: the API and its CSV files ----
FROM build AS publish
RUN dotnet publish src/OrderRouter.Api/OrderRouter.Api.csproj -c Release -o /app --no-build

# ---- Runtime: small ASP.NET image, non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app .

# The CSVs are published next to the app (/app), where the service looks by default.
# Set DATA_DIR and mount a volume to use different data files.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "OrderRouter.Api.dll"]
