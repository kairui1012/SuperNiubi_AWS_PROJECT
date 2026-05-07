FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["MyMvcApp.csproj", "./"]
RUN dotnet restore "MyMvcApp.csproj"

COPY . .
RUN dotnet publish "MyMvcApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Create the persistent DataProtection keys directory (will be mounted as volume)
RUN mkdir -p /var/propease/dataprotection-keys && chmod 755 /var/propease/dataprotection-keys

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MyMvcApp.dll"]