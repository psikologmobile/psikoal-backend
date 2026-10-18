import { expect, test } from '@playwright/test'

import { loginAsAdmin, navigateTo } from '../helpers/blazor'

/**
 * Panelin BÜTÜN sayfaları tek oturumda açılabiliyor mu?
 *
 * Neden gerekli: Playwright katmanı bir dönem yalnız Login ve Kategoriler'i
 * doğruluyordu. Diğer sekiz sayfa ne testle ne elle açılmıştı — ve o boşlukta
 * Dashboard TAMAMEN kırıktı: /admin/dashboard/stats 500 dönüyordu (Npgsql
 * `GroupBy(x => x.CreatedAt.UtcDateTime.Date)` ifadesini çeviremiyor). Belirti
 * sessizdi, çünkü sayfa hatayı yakalayıp boş kart gösteriyor.
 */

/** Nav'daki sıra — AdminNavigation.Items ile birebir. */
// `navLabel` AdminNavigation.Items'taki Label, `heading` ise sayfanın PageHeader
// başlığı. İkisi Ayarlar'da BİLEREK farklı ("Ayarlar" / "Sistem Ayarları"); tek
// alan kullanmak o sayfayı sessizce atlatırdı.
const PAGES = [
  { href: '/users', navLabel: 'Kullanıcılar', heading: 'Kullanıcılar' },
  { href: '/experts', navLabel: 'Uzmanlar', heading: 'Uzmanlar' },
  { href: '/listings', navLabel: 'İlanlar', heading: 'İlanlar' },
  { href: '/reviews', navLabel: 'Yorumlar', heading: 'Yorumlar' },
  { href: '/matches', navLabel: 'Eşleşmeler', heading: 'Eşleşmeler' },
  { href: '/categories', navLabel: 'Kategoriler', heading: 'Kategoriler' },
  { href: '/assessments', navLabel: 'Testler', heading: 'Testler' },
  { href: '/blogs', navLabel: 'Blog', heading: 'Blog Yazıları' },
  { href: '/notifications', navLabel: 'Bildirimler', heading: 'Bildirimler' },
  { href: '/settings', navLabel: 'Ayarlar', heading: 'Sistem Ayarları' },
  { href: '/audit-logs', navLabel: 'Denetim Kaydı', heading: 'Denetim Kaydı' },
  // Genel Bakış en SONA bırakıldı: giriş sonrası
  // zaten orada olduğumuz için ilk sırada tıklamak "aynı sayfaya gitme"
  // durumunu ölçer, gezinmeyi değil.
  { href: '/', navLabel: 'Genel Bakış', heading: 'Genel Bakış' },
] as const

test.describe('Admin paneli sayfaları', () => {
  // Tek test içinde tüm sayfalar geziliyor, her sayfa için ayrı test DEĞİL.
  // İki sebep: (1) AdminSessionState scoped bir servis, token yalnız Blazor
  // circuit'inin belleğinde — yeni bir test yeni bir circuit demek ve oturum
  // ölürdü; (2) her test bir login daha demek, API'nin auth politikası ise IP
  // başına dakikada 10 istek.
  test('tüm sayfalar tek oturumda açılır ve başlıkları görünür', async ({ page }) => {
    await loginAsAdmin(page)

    for (const { href, navLabel, heading } of PAGES) {
      // Gezinme NAV BAĞLANTISINA TIKLAYARAK: `page.goto` yeni circuit açar,
      // oturum ölür ve sayfa /login'e döner. Belirti "tablo bulunamadı" olur ve
      // yetki hatası sanılır (bkz. docs/E2E.md). navigateTo aynı zamanda
      // /login'e düşmediğimizi de doğruluyor.
      await navigateTo(page, navLabel)

      expect(new URL(page.url()).pathname, `${navLabel} yanlış adrese gitti`).toBe(href)

      // `.first()` ŞART: bazı sayfalarda aynı metinli ikinci bir başlık var
      // (örn. Testler'de sayfa başlığı + bölüm başlığı) ve Playwright'ın strict
      // mode'u iki eşleşmede hata veriyor. Hata mesajı "başlık görünmüyor" gibi
      // okunuyor, oysa başlık VAR — iki tane var.
      // `.page-title` sınıfı DENENDİ ve DOM'a çıkmıyor; rol tabanlı seçici
      // güvenilir olan.
      await expect(page.getByRole('heading', { name: heading }).first()).toBeVisible({
        timeout: 20_000,
      })
    }
  })

  test('dashboard sayaçları gerçek veriyle dolu gelir', async ({ page }) => {
    await loginAsAdmin(page)

    await expect(page.getByRole('heading', { name: 'Genel Bakış' }).first()).toBeVisible()

    // Sayaç kartlarının BULUNMASI yetmez, DEĞER taşımaları gerekir: uç 500
    // dönerken sayfa kartları çiziyor ama içleri boş kalıyordu.
    const values = page.locator('.stat-card-value')
    await expect(values.first()).toBeVisible({ timeout: 20_000 })

    const texts = await values.allInnerTexts()
    expect(texts.length).toBeGreaterThan(0)

    // Her kart bir SAYI göstermeli. Boş metin ya da "—" hatanın imzasıydı.
    for (const text of texts) {
      expect(text.trim(), 'sayaç kartı sayı göstermeli').toMatch(/^\d+$/)
    }

    // En az bir sayaç sıfırdan büyük olmalı: seed verisi basıldıktan sonra
    // totalUsers ve openListings kesinlikle dolu. Hepsi sıfırsa uç ya kırık ya
    // da yanlış veritabanına bakıyor — ikisi de bilinmesi gereken bir durum.
    expect(texts.some((text) => Number(text.trim()) > 0), 'tüm sayaçlar sıfır').toBe(true)
  })

  // Blog formu yazma yapmadan doğrulanır: başlıktan üretilen kısa ad mobilde
  // /blog/{slug} adresinin kendisi. Türkçe karakter katlaması bozulursa API
  // kaydı BLOG_SLUG_INVALID ile reddeder ve panel kullanılamaz hâle gelir.
  test('blog formu başlıktan Türkçe karakterleri katlayarak kısa ad üretir', async ({ page }) => {
    await loginAsAdmin(page)
    await navigateTo(page, 'Blog')

    await expect(page.getByRole('heading', { name: 'Blog Yazıları' }).first()).toBeVisible({
      timeout: 20_000,
    })
    await expect(page.getByTestId('blog-table')).toBeVisible()

    await page.getByTestId('blog-new').click()
    const title = page.getByTestId('blog-title')
    await title.fill('Çift Terapisinde İlk Görüşme: Neler Olur?')
    await title.blur()

    await expect(page.getByTestId('blog-slug')).toHaveValue('cift-terapisinde-ilk-gorusme-neler-olur', {
      timeout: 10_000,
    })
  })
})
