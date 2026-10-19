import { expect, test } from '@playwright/test'

import { loginAsAdmin, navigateTo } from '../helpers/blazor'

/**
 * Revize 2 admin sayfaları (docs/REVIZE-2.md §9) GERÇEK veriyle çalışıyor mu?
 *
 * admin-pages.spec.ts yalnız başlıkların açıldığını ölçer; burada her sayfanın ucuna
 * gerçekten bağlandığı doğrulanır. Yazma YAPILMAZ (dönem eklemek, fatura yüklemek
 * paylaşılan stack'i kalıcı değiştirirdi): fiyatlandırma önizlemesi kayıt yapmayan
 * bir hesap ucu, işlem kaydı ise yalnız okuma.
 *
 * Tek test, tek login: AdminSessionState circuit'e bağlı ve auth rate limit IP
 * başına dakikada 10 istek (bkz. admin-pages.spec.ts).
 */
test('Revize 2 sayfaları uçlarına bağlı: fiyatlandırma önizlemesi, işlem kaydı, ödemeler, ayarlar', async ({ page }) => {
  await loginAsAdmin(page)

  // Fiyatlandırma: migration'ın eklediği başlangıç dönemi "Geçerli" olarak listelenir.
  await navigateTo(page, 'Fiyatlandırma')
  const periods = page.getByTestId('pricing-table')
  await expect(periods.getByText('Geçerli').first()).toBeVisible({ timeout: 20_000 })

  // Önizleme: geçerli dönemle örnek ilanın başlangıç ücreti ve gün × teklif tablosu.
  await page.getByTestId('preview-run').click()
  await expect(page.getByTestId('preview-initial-fee')).toHaveText(/^₺[\d.]+$/, { timeout: 20_000 })
  await expect(page.getByTestId('pricing-preview').getByText('Onay günü')).toBeVisible()

  // İşlem kaydı: Kullanıcılar satırındaki bağlantı kişiyi seçili açar; her hesabın
  // en az bir "Kayıt" olayı vardır.
  await navigateTo(page, 'Kullanıcılar')
  await page.getByTestId('user-activity-link').first().click()
  await expect
    .poll(() => new URL(page.url()).pathname, { timeout: 20_000 })
    .toBe('/activity')
  await expect(page.getByTestId('activity-selected-user')).toBeVisible({ timeout: 20_000 })
  const timeline = page.getByTestId('activity-timeline')
  await expect(timeline).toBeVisible({ timeout: 20_000 })
  await expect(timeline.getByText('Hesap oluşturuldu')).toBeVisible()
  await expect(page.getByTestId('activity-export')).toBeEnabled()

  // Ödemeler: uç 200 dönmeli — tablo ya da boş durum, ama hata uyarısı değil.
  await navigateTo(page, 'Ödemeler ve Faturalar')
  await expect(page.getByTestId('payments-table')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('alert').filter({ hasText: /yetki|ulaşılamıyor|hata/i })).toHaveCount(0)

  // Anlaşma bildirimleri ve davetler: aynı ölçü.
  await navigateTo(page, 'Anlaşma Bildirimleri')
  await expect(page.getByTestId('claims-table')).toBeVisible({ timeout: 20_000 })
  await navigateTo(page, 'Davetler')
  await expect(page.getByTestId('referrals-table')).toBeVisible({ timeout: 20_000 })

  // Ayarlar: Revize 2 anahtarları tipli düzenleyiciyle gelir (açık/kapalı seçimi).
  await navigateTo(page, 'Ayarlar')
  await expect(page.getByTestId('setting-CLAIMS_ENABLED')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByTestId('setting-TOPUP_PRESETS_TRY')).toBeVisible()
})
