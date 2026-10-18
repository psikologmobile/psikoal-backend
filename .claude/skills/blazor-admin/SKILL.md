---
description: Build Blazor components, pages, and layouts for the PsikoAl admin panel (PsikoAl.Client) using the ShadCn.Blazor design system
---

# Blazor Admin Skill — PsikoAl Admin Panel

## Overview

`src/PsikoAl.Client` (Blazor Server, net10.0) admin paneli **ShadCn.Blazor** bileşen kütüphanesi ile yazılır. Bu skill, yeni sayfa/bileşen eklerken uyulacak kalıpları ve kütüphanenin bilinen tuzaklarını içerir.

## When to Use

- `PsikoAl.Client` içinde yeni sayfa, bileşen veya layout oluştururken
- Mevcut admin sayfalarını düzenlerken (Users, Experts, Listings, Reviews, Matches, Claims, UserActivity, Categories, Assessments, Blogs, Pricing, Payments, Referrals, Notifications, SystemSettings)
- Onay kuyrukları, CRUD ekranları, dashboard kartları veya grafik eklerken

## Core References

Kod yazmadan önce oku:

| Doküman | Amaç |
| --- | --- |
| `docs/ADMIN_PANEL_REQUIREMENTS.md` | Onay akışları (ilan/uzman/yorum), CRUD kuralları, rol yetkileri, dashboard metrikleri, audit log zorunluluğu |
| `CLAUDE.md` (PART 2) | Marka rengi (Sky #0EA5E9), flat design (gölge yok) |

## Kurulum (zaten yapılmış — referans)

- NuGet: `ShadCn.Blazor.Components` (+ transitif `ShadCn.Blazor.Theme.Default`)
- `Components/App.razor`: `theme.css` **app.css'ten önce** yüklenir (override sırası)
- `Program.cs`: `builder.Services.AddShadCnBlazorComponents()`
- `Components/_Imports.razor`: bileşen namespace'leri (`ShadCn.Blazor.Components.Button` vb.)
- `App.razor`: `<Routes @rendermode="InteractiveServer" />` — **global interaktivite**. Sayfalara ayrıca `@rendermode` yazılmaz; layout'taki butonların çalışması buna bağlı.

## ⚠️ Kritik Tuzaklar (hepsi bu projede yaşandı)

### 1. String parametrelere `@bind-Value` kullan

Razor'da string tipli component parametresine `Value="_alan"` yazmak **literal string** geçirir (`value="_alan"` çıktısı verir).

```razor
❌ <Select Value="_status" ValueChanged="v => _status = v">
✅ <Select @bind-Value="_status">
✅ <Input Value="@_editValues.GetValueOrDefault(key)" ValueChanged="v => ..." />  @* @ ile ifade *@
```

### 2. Chart: SVG üreten bileşenler HTML üretenlerden ÖNCE gelmeli

`ChartTooltip` ve `ChartLegend` HTML `<div>` üretir. HTML ayrıştırıcısı `<svg>` foreign-content içinde bir `<div>` görünce SVG'yi erkenden kapatır — sonrasında gelen `Bar`/`Line`/`Area` elemanları SVG bağlamının dışında kalır ve **hiç çizilmez** (grafik boş görünür, hata vermez).

```razor
✅ <BarChart Data="_data" XDataKey="Gun">
       <CartesianGrid Vertical="false" />
       <XAxis DataKey="Gun" /> <YAxis />
       <Bar DataKey="Ilan" Fill="var(--color-Ilan)" Radius="4" />
       <ChartTooltip /> <ChartLegend />   @* HTML üretenler EN SONDA *@
   </BarChart>
```

### 3. `Link` bileşeni kullanılmaz

`<Link>` HTML'in void `<link>` elementiyle çakışır, derleme hatası verir. Bunun yerine `<a class="link-button" href="...">` kullan.

### 4. Tailwind utility'leri sınırlıdır

`theme.css` genel bir Tailwind katmanı DEĞİL — yalnızca kütüphanenin kendi bileşenlerinin kullandığı ~550 sınıfı içerir. `grid-cols-2`, `gap-6`, `bg-sky-500` gibi sınıflar **yoktur**.

- Mevcut olanlar (güvenle kullanılabilir): `flex`, `flex-col`, `gap-1..4`, `space-y-*`, `p-*`, `px-*`, `py-*`, `mb-*`, `mt-*`, `w-full`, `w-24`, `w-48`, `max-w-sm/md/lg/xs`, `text-xs/sm/lg/xl/2xl`, `text-muted-foreground`, `text-right`, `text-center`, `font-medium/semibold/bold/mono`, `rounded-lg/md/full`, `border`, `bg-card`, `bg-muted`, `truncate`, `overflow-hidden`
- Bunların dışında bir düzen gerekiyorsa `wwwroot/app.css`'e **kendi semantik sınıfını** yaz ve `hsl(var(--token))` kullan — hex hardcode etme.

### 5. Culture bug (kütüphane kaynaklı, bilgi amaçlı)

Kütüphane `animation-delay` değerlerini kültüre göre formatlıyor (`0,05s`). Türkçe kültürde geçersiz CSS üretir; tarayıcı o satırı atar, çubuklar aynı anda belirir. Fonksiyonel etkisi yok, dokunma.

## Tema

Renkler `wwwroot/app.css` içindeki `:root` bloğunda **bare HSL triplet** olarak override edilir (shadcn konvansiyonu), kullanımda `hsl(var(--x))` ile sarmalanır.

- `--primary: 198.6 88.7% 48.4%` (Sky #0EA5E9), `--background: 240 24% 96%` (iOS gri), `--card: 0 0% 100%`
- Grafik serileri: `--chart-1..5`
- Durum renkleri: `--pa-success/warning/danger/info/neutral` (+ `-soft` varyantları)
- **Flat design:** `.shadow-sm` override ile sıfırlanmıştır. Kartlarda gölge yok; yüzen katmanlar (dialog, dropdown) ayrışmaya devam eder.

## Shared Bileşenler — önce bunları kullan

`Components/Shared/` altındaki sarmalayıcılar ham ShadCn bileşenlerine tercih edilir; kütüphanede bir sorun çıkarsa tek noktadan yamalanır.

| Bileşen | Kullanım |
| --- | --- |
| `PageHeader` | Sayfa başlığı + açıklama + sağda aksiyon slotu |
| `StatusChip` | Domain durumu → renk + Türkçe etiket (tüm status'lar tek sözlükte) |
| `StatCard` | Dashboard metrik kartı, opsiyonel `Href` ile tıklanabilir |
| `EmptyState` | Boş liste durumu |
| `LoadingSkeleton` | Tablo yükleniyor durumu |
| `ReasonDialog` | **Sebep zorunlu** aksiyonlar (red, zorla sonlandırma) |
| `ConfirmDialog` | Basit onay (dondurma vb.) |
| `NavIcon` | Inline SVG ikon seti (Lucide yolları) |

Yeni bir durum değeri eklenirse `StatusChip.razor` içindeki `Descriptors` sözlüğüne eklenir — sayfalarda ayrı etiket/renk fonksiyonu yazılmaz.

## Sayfa Kalıbı

```razor
@page "/ornek"
@inject IAdminClientService AdminClient

<PageTitle>Başlık — PsikoAl Admin</PageTitle>

<PageHeader Title="Başlık" Description="Kısa açıklama." />

<div class="toolbar">
    <Select Class="toolbar-select" @bind-Value="_status">
        <option value="pending">Onay Bekleyen</option>
    </Select>
    <Button Disabled="_isLoading" OnClick="LoadAsync">Filtrele</Button>
</div>

@if (_errorMessage is not null)
{
    <Alert Variant="AlertVariant.Destructive" Class="mb-4">
        <AlertDescription>@_errorMessage</AlertDescription>
    </Alert>
}

<Card Class="table-card">
    @if (_isLoading)      { <LoadingSkeleton /> }
    else if (_items is { Count: 0 }) { <EmptyState Title="Kayıt bulunamadı" /> }
    else if (_items is not null)
    {
        <Table>
            <TableHeader>
                <TableRow><TableHead>Ad</TableHead><TableHead Class="text-right">İşlem</TableHead></TableRow>
            </TableHeader>
            <TableBody>
                @foreach (var item in _items)
                {
                    <TableRow>
                        <TableCell>@item.Name</TableCell>
                        <TableCell Class="cell-actions">
                            <Button Variant="ButtonVariant.Outline" Size="ButtonSize.Sm"
                                    OnClick="() => ToggleDetailAsync(item.Id)">İncele</Button>
                        </TableCell>
                    </TableRow>

                    @* Genişleyen detay: tam genişlik satır — Sheet/Dialog'a taşıma, @code mantığı bozulur *@
                    @if (_openId == item.Id && _detail is not null)
                    {
                        <TableRow>
                            <TableCell Class="detail-cell" colspan="2">
                                <div class="detail-panel">
                                    <dl class="detail-list"><dt>Alan</dt><dd>@_detail.Value</dd></dl>
                                    <div class="detail-actions">…</div>
                                </div>
                            </TableCell>
                        </TableRow>
                    }
                }
            </TableBody>
        </Table>
    }
</Card>

@code {
    private bool _isLoading = true;   // ilk render'da skeleton görünsün

    // Oturum kontrolü sayfada YAPILMAZ: MainLayout oturumu geri yükleyip doğrulamadan
    // @Body'yi render etmez, rolün açamayacağı sayfada da yetki uyarısı gösterir.
    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await AdminClient.ListXAsync(_status, CancellationToken.None);
        _items = result.Value;
        _errorMessage = result.ErrorMessage;   // API'nin Türkçe mesajı (AdminApiResult)
    }
}
```

Auth gerektirmeyen sayfalar (`/login`, `/not-found`, `/Error`) `@layout PsikoAl.Client.Components.Layout.BareLayout` kullanır.

## Yerel CSS Sınıfları (`app.css`)

`toolbar`, `toolbar-search`, `toolbar-select`, `table-card`, `cell-actions`, `detail-cell`, `detail-panel`, `detail-columns`, `detail-list`, `detail-actions`, `detail-heading`, `field`, `form-grid`, `stack-3`, `stat-grid`, `switch-field`, `link-button`, `muted`, `chart-card-body`, `auth-shell`, `auth-card`, `wallet-summary`, `field-error`, `help-text`, `formula`, `row-current`, `section-title`, `preview-figures`, `timeline`, `user-picker`

## Do NOT

- Hex renk hardcode etme — `hsl(var(--token))` kullan
- `theme.css`'te olmayan Tailwind sınıfı yazma (§4)
- String parametreye `@bind-Value` olmadan değişken bağlama (§1)
- Grafikte `Bar`/`Line`'ı `ChartTooltip`/`ChartLegend`'dan sonra yazma (§2)
- `<Link>` bileşenini kullanma (§3)
- Sayfalara `@rendermode` ekleme (global olarak ayarlı)
- Genişleyen detay satırını Sheet/Dialog'a taşıma (mevcut `@code` state mantığını bozar)
- Red/sonlandırma aksiyonunu sebep zorunluluğu olmadan yazma — `ReasonDialog` kullan
- Audit log gerektiren işlemleri (`docs/ADMIN_PANEL_REQUIREMENTS.md` §6) sessizce geçme
- UI metinlerini İngilizceye çevirme — panel tamamen Türkçedir

## Saat, para ve dosya yardımcıları

- Tarih/saat gösterimi `AdminFormat.DateTime(...)` ile (her zaman Europe/Istanbul); `ToLocalTime()` sunucu saat dilimine bağlıdır, yeni kodda kullanma. Form girdisi: `AdminFormat.ParseLocalDateTime` / `ParseLocalDate`.
- Para etiketleri API'den hazır gelir (`*Label`); TL girdisini kuruşa `AdminFormat.ParseTryToKurus` çevirir.
- Dosya indirme / yeni sekmede açma `AdminFileInterop` (wwwroot/js/admin-files.js modülü) ile — CSP `script-src 'self'`, satır içi betik çalışmaz.
