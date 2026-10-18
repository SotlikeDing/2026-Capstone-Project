# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy entire solution
COPY . .

# Restore and build WebAPI
RUN dotnet restore "src/WebAPI/WebAPI/WebAPI.csproj"
WORKDIR /src/src/WebAPI
RUN dotnet publish -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 80
EXPOSE 443
COPY --from=build /app/publish .

# Persistent home for a RETRAINED price-model bundle uploaded to the running API (POST api/aihelper/admin/upload-model, see
# tools/ai/retrain.py --upload). The bundle baked into the image (/app/AiModels) is always available; the newer of the two is used.
# Declared as a VOLUME so a retrained model also survives `docker compose up -d` recreating the container when the compose file has
# no explicit mount; mount a named volume here for a durable, inspectable location (see docker-compose.prod-local.yml).
ENV AiHelper__ModelOverrideDirectory=/data/ai-models
RUN mkdir -p /data/ai-models
VOLUME ["/data/ai-models"]

ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "WebAPI.dll"]