# One image: the React client is built, then served as static files by the .NET API.

# ---- client ----
FROM node:24-alpine AS client
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
# Map style is baked in at build time (Vite env var).
ARG VITE_MAP_STYLE_URL=https://tiles.openfreemap.org/styles/liberty
ENV VITE_MAP_STYLE_URL=$VITE_MAP_STYLE_URL
RUN npm run build

# ---- server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY server/BluffOrBlighty.Api/BluffOrBlighty.Api.csproj server/BluffOrBlighty.Api/
RUN dotnet restore server/BluffOrBlighty.Api/BluffOrBlighty.Api.csproj
COPY server/BluffOrBlighty.Api/ server/BluffOrBlighty.Api/
RUN dotnet publish server/BluffOrBlighty.Api/BluffOrBlighty.Api.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=server /app ./
COPY --from=client /src/client/dist ./wwwroot
# Cloud Run sends traffic to $PORT (8080 by default).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BluffOrBlighty.Api.dll"]
