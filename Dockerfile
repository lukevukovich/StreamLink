FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY StreamLink/StreamLink.csproj StreamLink/
RUN dotnet restore StreamLink/StreamLink.csproj

COPY StreamLink/ StreamLink/
RUN dotnet publish StreamLink/StreamLink.csproj \
    -c Release \
    --no-restore \
    -o /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/data/keys

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ConnectionStrings__StreamLink="Data Source=/app/data/streamlink.db" \
    DataProtection__KeyDirectory=/app/data/keys

EXPOSE 8080

CMD ["sh", "-c", "exec dotnet StreamLink.dll --urls http://0.0.0.0:${PORT:-8080}"]