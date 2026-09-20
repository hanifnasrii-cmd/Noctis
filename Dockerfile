# noctis-server: headless Noctis (OpenSubsonic API + library scanner + device sync).
#
#   docker build -t noctis-server .
#   docker run -d --name noctis -p 4747:4747 \
#     -v /path/to/music:/music:ro -v noctis-data:/data noctis-server
#   docker exec -it noctis /app/noctis-server user add alice
#
# The container speaks plain HTTP on 4747 (NOCTIS_TLS=0): terminate TLS in your
# reverse proxy. Set NOCTIS_TLS=1 to have it use its own self-signed certificate
# instead (the fingerprint is printed at start; pin it in the client).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/Noctis.Core/Noctis.Core.csproj src/Noctis.Core/
COPY src/Noctis.Server/Noctis.Server.csproj src/Noctis.Server/
RUN dotnet restore src/Noctis.Server/Noctis.Server.csproj -a "$TARGETARCH"
COPY src/Noctis.Core/ src/Noctis.Core/
COPY src/Noctis.Server/ src/Noctis.Server/
RUN dotnet publish src/Noctis.Server/Noctis.Server.csproj -c Release -a "$TARGETARCH" \
      --no-restore --self-contained false -p:PublishReadyToRun=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV NOCTIS_DATA_DIR=/data \
    NOCTIS_MUSIC=/music \
    NOCTIS_PORT=4747 \
    NOCTIS_TLS=0 \
    DOTNET_gcServer=0
VOLUME ["/data", "/music"]
EXPOSE 4747
ENTRYPOINT ["/app/noctis-server"]
CMD ["serve"]
