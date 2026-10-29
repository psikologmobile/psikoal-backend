# Psikosirius API — Render (runtime: docker) ve lokal `docker build` için.
# Build bağlamı REPO KÖKÜDÜR:  docker build -f src/PsikoAl.Api/Dockerfile .
#
# SDK etiketi global.json ile aynı özellik bandına sabit: `sdk:10.0` kayan etiketi yeni bir
# banda geçtiğinde global.json'daki rollForward=latestPatch build'i kırar ve
# TreatWarningsAsErrors yeni analyzer uyarılarını hataya çevirir.

FROM mcr.microsoft.com/dotnet/sdk:10.0.200 AS build
WORKDIR /repo

# Önce yalnız proje dosyaları: kod değişince restore katmanı önbellekten gelir.
COPY global.json ./
COPY src/Directory.Build.props src/
COPY src/PsikoAl.Common/PsikoAl.Common.csproj src/PsikoAl.Common/
COPY src/PsikoAl.Data/PsikoAl.Data.csproj src/PsikoAl.Data/
COPY src/PsikoAl.Services/PsikoAl.Services.csproj src/PsikoAl.Services/
COPY src/PsikoAl.Api/PsikoAl.Api.csproj src/PsikoAl.Api/
RUN dotnet restore src/PsikoAl.Api/PsikoAl.Api.csproj

COPY src/PsikoAl.Common/ src/PsikoAl.Common/
COPY src/PsikoAl.Data/ src/PsikoAl.Data/
COPY src/PsikoAl.Services/ src/PsikoAl.Services/
COPY src/PsikoAl.Api/ src/PsikoAl.Api/
RUN dotnet publish src/PsikoAl.Api/PsikoAl.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Render istekleri PORT ortam değişkenindeki porta yönlendirir; render.yaml PORT=8080 verir.
# HTTPS portu bilerek TANIMLANMAZ: TLS Render'ın kenarında biter, UseHttpsRedirection
# konteyner içinde no-op kalır (sağlık kontrolü yönlendirmeye takılmaz).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "PsikoAl.Api.dll"]
