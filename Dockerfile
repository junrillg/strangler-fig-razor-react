FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS sdk
# PowerShell is not used by this Make/.NET toolchain; remove its separately serviced payload.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends --only-upgrade zlib1g=1:1.3.dfsg-3.1ubuntu2.3 \
    && rm -rf /var/lib/apt/lists/* /usr/share/powershell /usr/bin/pwsh

FROM sdk AS build
ARG APP=Monolith
WORKDIR /source
COPY . .
RUN dotnet restore "src/${APP}/${APP}.csproj" --locked-mode
RUN dotnet publish "src/${APP}/${APP}.csproj" --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends --only-upgrade zlib1g=1:1.3.dfsg-3.1ubuntu2.3 \
    && rm -rf /var/lib/apt/lists/*
ARG APP=Monolith
WORKDIR /app
COPY --from=build --chown=1654:1654 /publish .
ENV ASPNETCORE_HTTP_PORTS=8080 APP_DLL=${APP}.dll
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 CMD dotnet "$APP_DLL" --healthcheck
CMD ["/bin/sh", "-c", "exec dotnet \"$APP_DLL\""]
