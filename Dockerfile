FROM node:24-alpine AS web-build
WORKDIR /source/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /source
COPY AgentPlaza.slnx ./
COPY src/AgentPlaza.Api/AgentPlaza.Api.csproj src/AgentPlaza.Api/
COPY src/AgentPlaza.Domain/AgentPlaza.Domain.csproj src/AgentPlaza.Domain/
COPY src/AgentPlaza.Infrastructure/AgentPlaza.Infrastructure.csproj src/AgentPlaza.Infrastructure/
RUN dotnet restore AgentPlaza.slnx
COPY src/ src/
RUN dotnet publish src/AgentPlaza.Api/AgentPlaza.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R $APP_UID:$APP_UID /home/app/.aspnet
COPY --from=api-build /app ./
COPY --from=web-build /source/web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AgentPlaza.Api.dll"]
