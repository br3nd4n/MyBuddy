# Multi-stage Dockerfile
# Stage 1: build WASM (wasm-pack) using Rust
FROM rust:1.71-bullseye AS wasm_builder

# Install wasm-pack installer dependencies
RUN apt-get update && apt-get install -y curl build-essential pkg-config ca-certificates && rm -rf /var/lib/apt/lists/*

# Install wasm-pack
RUN curl https://rustwasm.github.io/wasm-pack/installer/init.sh -sSf | sh

WORKDIR /src/wasm-client

# Copy only the wasm-client source into this stage to make cache effective
COPY wasm-client/Cargo.toml ./Cargo.toml
COPY wasm-client/src ./src
COPY wasm-client/static ./static

# Ensure target and build wasm package
RUN rustup target add wasm32-unknown-unknown \
    && wasm-pack build --release --target web --out-dir ./static/pkg

# Stage 2: build .NET publish artifact
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy everything - we will copy the prebuilt wasm static from wasm_builder
COPY . .

# Copy wasm static output from wasm_builder into the build context so it can be published into wwwroot
COPY --from=wasm_builder /src/wasm-client/static /src/wasm-client/static

# Publish the ASP.NET Core app
RUN dotnet publish "server/RealTimeServer.csproj" -c Release -o /app/publish

# Stage 3: runtime
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app
EXPOSE 5000

# Copy published app
COPY --from=build /app/publish .

# Copy static files (wasm, pkg, index.html) into wwwroot so the ASP.NET static file middleware will serve them
COPY --from=build /src/wasm-client/static /app/wwwroot

ENTRYPOINT ["dotnet", "RealTimeServer.dll"]