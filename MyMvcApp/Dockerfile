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

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MyMvcApp.dll"]