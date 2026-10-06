FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/Shop.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Non-root user that ships with the image.
USER $APP_UID
# "migrate" as the only argument runs the database migration and exits (used by the migrate job).
ENTRYPOINT ["dotnet", "Shop.Api.dll"]
