FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["webapi/CopilotChatWebApi.csproj", "webapi/"]
COPY ["shared/CopilotChatShared.csproj", "shared/"]
RUN dotnet restore "webapi/CopilotChatWebApi.csproj"
COPY webapi/. webapi/
COPY shared/. shared/
WORKDIR "/src/webapi"
RUN dotnet build "CopilotChatWebApi.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "CopilotChatWebApi.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CopilotChatWebApi.dll"]
