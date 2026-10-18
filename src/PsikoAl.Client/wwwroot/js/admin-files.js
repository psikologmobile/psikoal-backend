// Panel dosya yardımcıları (JS interop modülü). CSP "script-src 'self'" olduğu için
// satır içi betik yerine bu dosya import edilir.

// Metni dosya olarak indirir. CSV için BOM eklenir: Excel UTF-8'i ancak BOM ile tanır,
// yoksa Türkçe karakterler bozuk görünür.
export function downloadText(fileName, content, mimeType) {
  const blob = new Blob(['﻿', content], { type: mimeType })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

// İmzalı fatura adresini yeni sekmede açar. Açılır pencere engellenirse false döner;
// sayfa o durumda bağlantıyı elle tıklanacak şekilde gösterir. "noopener" özelliği
// window.open'ın her zaman null dönmesine yol açtığı için opener elle kesilir.
export function openInNewTab(url) {
  const opened = window.open(url, '_blank')
  if (!opened) {
    return false
  }
  opened.opener = null
  return true
}
