# Psikosirius Admin Paneli (Blazor Server) — Render (runtime: docker) ve lokal `docker build` için.
# Build bağlamı REPO KÖKÜDÜR:  docker build -f src/PsikoAl.Client/Dockerfile .
#
# SDK etiketi neden sabit: bkz. src/PsikoAl.Api/Dockerfile.

FROM mcr.microsoft.com/dotnet/sdk:10.0.200 AS build
WORKDIR /repo

COPY global.json ./
COPY src/Directory.Build.props src/
COPY src/PsikoAl.Common/PsikoAl.Common.csproj src/PsikoAl.Common/
COPY src/PsikoAl.Client/PsikoAl.Client.csproj src/PsikoAl.Client/
RUN dotnet restore src/PsikoAl.Client/PsikoAl.Client.csproj

COPY src/PsikoAl.Common/ src/PsikoAl.Common/
COPY src/PsikoAl.Client/ src/PsikoAl.Client/
# --no-restore yok: Blazor static web assets publish'te kendi restore grafiğine bakıyor.
RUN dotnet publish src/PsikoAl.Client/PsikoAl.Client.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "PsikoAl.Client.dll"]
