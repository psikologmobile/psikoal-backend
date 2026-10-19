// Demo verisini GERÇEK uç noktalar üzerinden basar ve her çağrıyı raporlar.
//
// KULLANIM:  node scripts/seed-demo.mjs
// Ön koşul:  scripts/e2e-stack.sh ile stack ayakta (API 5089, Supabase 54321/54322).
//
// Neden SQL değil de HTTP: amaç veriyi oluşturmak DEĞİL, veriyi oluşturan yolun
// çalıştığını kanıtlamak. SQL ile INSERT etmek validator'ları, yetki kontrollerini,
// state machine'i ve audit log'u tamamen atlar — yani test edilmek istenen her şeyi.
//
// Zincir:
//   admin kategori ekler → uzman kaydolur + profil açar → admin onaylar/doğrular
//   → admin uzmanlara TL bakiye hediye eder (teklif ücretlidir)
//   → danışan ilan açar → admin onaylar → uzman ücreti sorar + teklif verir
//   → danışan "eşleşti" işaretler → danışan yorum yazar → admin yorumu onaylar
//   → admin bildirim gönderir → admin blog yazılarını yayınlar/planlar

import { deflateSync } from 'node:zlib'

const API = process.env.E2E_API_URL ?? 'http://localhost:5089'

const ADMIN = { email: 'e2e-admin@psikoal.test', password: 'E2eAdminPass123!' }

// Teklif ücreti dönemin tavanını (başlangıç dönemi 1000 TL) aşmaz; 5000 TL demo
// zincirindeki teklifler ve elle denemeler için fazlasıyla yeter.
const DEMO_EXPERT_BALANCE_KURUS = 500_000

// DEMO danışanı, E2E danışanından AYRI olmak zorunda.
// e2e-client@psikoal.test hesabı .NET E2E testlerinin paylaşılan fixture'ıdır ve
// SystemUsersChainTests LISTING_MAX_ACTIVE'i 1'e çekip o hesapla ölçüm yapıyor —
// demo ilanları oraya basılırsa o test "danışanın zaten 3 aktif ilanı var" diye
// kırmızı olur. Belirti alakasız görünür, sebep tamamen saklanır.
const CLIENT = {
  email: 'demo-client@psikoal.test',
  password: 'DemoClient123!',
  firstName: 'Ayşe',
  lastName: 'Yılmaz',
}

// Kanonik kategori listesi migration'ın İÇİNDE seed'li:
//   supabase/migrations/20260806192233_categories_reviews.sql:28
// Buradaki liste onun aynası; ayrışırlarsa seed 422 verir ve sebep görünür.
// DİKKAT: native-atomic/mock-db/data/categories.json BAYAT — 7 slug'ı farklı
// (travma-ptsd / iliski-sorunlari / uyku-bozuklugu / stres-yonetimi /
// motivasyon / is-stresi / yas-kayip). Oradan seed etmek çift kayıt üretir.
const CANONICAL_CATEGORIES = [
  'anksiyete', 'depresyon', 'iliski-problemleri', 'travma-tssb', 'bagimlilik',
  'aile-terapisi', 'cift-terapisi', 'cocuk-ergen', 'kisilik-bozukluklari',
  'yeme-bozuklugu', 'uyku-sorunlari', 'ofke-yonetimi', 'ozguven-benlik',
  'kayip-yas', 'kariyer-is-stresi', 'panik-atak', 'okb',
]

// specializations ve specialization alanları kategori SLUG'ını değil ADINI tutar
// (ExpertService/ListingService → CategoryService.AllActiveNamesExistAsync).
// Mobil de aynısını yapıyor: OnboardingStepSpecs.tsx onToggleSpec(category.name).
const CAT = {
  anksiyete: 'Anksiyete ve Kaygı',
  depresyon: 'Depresyon',
  iliski: 'İlişki Problemleri',
  cift: 'Çift Terapisi',
  aile: 'Aile Terapisi',
  panik: 'Panik Atak',
  okb: 'OKB',
  kayip: 'Kayıp ve Yas',
  ozguven: 'Özgüven ve Benlik',
  kariyer: 'Kariyer ve İş Stresi',
}

const EXPERTS = [
  {
    email: 'demo-expert-1@psikoal.test',
    password: 'DemoExpert123!',
    firstName: 'Elif',
    lastName: 'Kaya',
    profile: {
      title: 'Klinik Psikolog',
      specializations: [CAT.anksiyete, CAT.panik, CAT.okb],
      experienceYears: 9,
      bio: 'Bilişsel davranışçı terapi ağırlıklı çalışıyorum. Anksiyete bozuklukları, panik atak ve obsesif kompulsif bozukluk alanlarında dokuz yıldır yetişkinlerle seans yürütüyorum. Seanslarımda somut ödevler ve ölçülebilir hedefler kullanırım.',
      avatarUrl: 'https://images.psikoal.test/avatars/elif-kaya.jpg',
      education: 'İstanbul Üniversitesi — Klinik Psikoloji Yüksek Lisans',
      personalWebsite: 'https://elifkaya.psikoal.test',
    },
  },
  {
    email: 'demo-expert-2@psikoal.test',
    password: 'DemoExpert123!',
    firstName: 'Mehmet',
    lastName: 'Demir',
    profile: {
      title: 'Uzman Psikolog',
      specializations: [CAT.depresyon, CAT.kayip, CAT.ozguven],
      experienceYears: 14,
      bio: 'Şema terapi ve kabul kararlılık terapisi yaklaşımlarını birlikte kullanıyorum. Depresyon, yas süreçleri ve benlik değeri sorunları üzerine on dört yıllık klinik deneyimim var. Uzun soluklu terapi süreçlerinde çalışmayı tercih ediyorum.',
      avatarUrl: 'https://images.psikoal.test/avatars/mehmet-demir.jpg',
      education: 'Hacettepe Üniversitesi — Psikoloji Doktora',
      personalWebsite: null,
    },
  },
  {
    email: 'demo-expert-3@psikoal.test',
    password: 'DemoExpert123!',
    firstName: 'Zeynep',
    lastName: 'Aksoy',
    profile: {
      title: 'Aile ve Çift Terapisti',
      specializations: [CAT.cift, CAT.aile, CAT.iliski],
      experienceYears: 6,
      bio: 'Sistemik aile terapisi eğitimi aldım ve çiftlerle iletişim çatışmaları üzerine çalışıyorum. İlişki krizleri, ebeveynlik uyuşmazlıkları ve ayrılık süreçlerinde altı yıldır danışanlara eşlik ediyorum. Seanslarımı çoğunlukla çift olarak yürütürüm.',
      avatarUrl: null,
      education: 'Bahçeşehir Üniversitesi — Aile Danışmanlığı Yüksek Lisans',
      personalWebsite: null,
    },
  },
]

const LISTINGS = [
  {
    title: 'Panik atak ataklarım için haftalık online destek arıyorum',
    description:
      'Yaklaşık altı aydır beklenmedik anlarda çarpıntı ve nefes darlığı yaşıyorum. Toplu taşımada başlıyor, sonrasında gün boyu tedirginlik sürüyor. BDT ile çalışan, akşam saatlerinde online görüşebilecek bir uzman arıyorum.',
    specialization: [CAT.panik, CAT.anksiyete],
    budgetMin: 800,
    budgetMax: 1400,
    preferredSessionType: 'online',
    city: 'İstanbul',
    approve: true,
  },
  {
    title: 'İş yerindeki tükenmişlik ve motivasyon kaybı için destek',
    description:
      'Son bir yıldır işe gitmek istemiyorum, sabahları kalkmakta zorlanıyorum. Kariyer değişikliği düşünüyorum ama karar veremiyorum. Yüz yüze veya online fark etmez, hafta içi gündüz uygunum.',
    specialization: [CAT.depresyon, CAT.kariyer],
    budgetMin: 1000,
    budgetMax: 1800,
    preferredSessionType: 'yüz_yüze_online',
    city: 'Ankara',
    approve: true,
  },
  {
    title: 'Eşimle iletişim sorunlarımız için çift terapisi',
    description:
      'Üç yıllık evliyiz, son aylarda her konuşma tartışmaya dönüyor. İkimiz de sürece açığız. Hafta sonu yüz yüze görüşebileceğimiz bir çift terapisti arıyoruz.',
    specialization: [CAT.cift, CAT.iliski],
    budgetMin: 1500,
    budgetMax: 2500,
    preferredSessionType: 'yüz_yüze',
    city: 'İzmir',
    approve: true,
  },
  {
    title: 'Yakın kaybı sonrası yas sürecinde destek almak istiyorum',
    description:
      'Babamı üç ay önce kaybettim. Günlük hayata dönmekte zorlanıyorum ve çevremdekilerle paylaşmak istemiyorum. Online görüşmeyi tercih ederim.',
    specialization: [CAT.kayip],
    budgetMin: 700,
    budgetMax: 1200,
    preferredSessionType: 'online',
    city: 'İstanbul',
    approve: false, // bilinçli: mobilde PENDING_APPROVAL durumu görünsün
  },
]

// Blog yazıları admin uçlarından basılır: oluştur (taslak) → kapak yükle →
// yayınla ya da planla. `category` kanonik kategori SLUG'ı; mobil kategori
// sayfası GET /blogs?category={blogTag} ile bu bağ üzerinden süzer.
// İçerik düz metin, paragraflar boş satırla ayrılır (app/blog/[slug].tsx
// content.split('\n\n') yapıyor).
const BLOGS = [
  {
    category: 'anksiyete',
    tags: ['kendinebakım', 'teknikler'],
    title: 'Kaygı Anında Bedeninizi Sakinleştirmenin 4 Yolu',
    excerpt:
      'Kaygı yükseldiğinde zihin "tehlike var" der, beden de buna inanır. Nefes, kas gevşetme ve dikkat odağını değiştirme gibi basit tekniklerle bu alarmı birkaç dakika içinde kısabilirsiniz.',
    content: [
      'Kaygı, bedenin bizi korumak için geliştirdiği eski bir alarm sistemidir. Kalp hızlanır, nefes sıklaşır, kaslar gerilir. Sorun, bu alarmın gerçek bir tehlike yokken de çalabilmesidir. İyi haber şu: bedeni sakinleştirdiğinizde zihin de onu takip eder.',
      'Birinci yol diyafram nefesidir. Bir elinizi göğsünüze, diğerini karnınıza koyun. Burnunuzdan dört saniyede nefes alın, karnınızın şiştiğini hissedin; altı saniyede ağzınızdan verin. Nefes verişin alıştan uzun olması parasempatik sinir sistemini devreye sokar.',
      'İkinci yol kademeli kas gevşetmedir. Ellerinizi beş saniye sıkıp bırakın, aradaki farkı fark edin. Aynısını omuzlar, yüz ve bacaklar için tekrarlayın. Gerginliği bilinçli olarak yaratıp bırakmak, bedene gevşemenin nasıl bir his olduğunu hatırlatır.',
      'Üçüncü yol 5-4-3-2-1 tekniğidir: Gördüğünüz beş şeyi, duyduğunuz dört sesi, dokunabildiğiniz üç yüzeyi, aldığınız iki kokuyu ve bir tadı sayın. Dikkat, kaygı veren düşünceden şimdiki ana taşınır.',
      'Dördüncü yol düzenli harekettir. Haftada üç gün otuz dakikalık tempolu yürüyüş bile kaygı belirtilerini belirgin biçimde azaltır. Bu teknikler işe yaramıyorsa ya da kaygı günlük yaşamınızı kısıtlıyorsa bir uzmandan destek almak en doğru adımdır; Bilişsel Davranışçı Terapi kaygı bozukluklarında güçlü bir kanıt tabanına sahiptir.',
    ].join('\n\n'),
    author: { name: 'Uzm. Psk. Elif Kaya', title: 'Klinik Psikolog' },
    color: [14, 165, 233],
  },
  {
    category: 'uyku-sorunlari',
    tags: ['uykuhijyeni', 'ruhsağlığı'],
    title: 'Uyku Hijyeni: Daha İyi Uyumak İçin Küçük Alışkanlıklar',
    excerpt:
      'Uyku ile ruh sağlığı arasındaki ilişki iki yönlüdür: kötü uyku kaygıyı ve çökkün ruh halini artırır, onlar da uykuyu bozar. Döngüyü kırmanın ilk adımı akşam rutininizdedir.',
    content: [
      'Uykusuz geçen bir gecenin ertesinde her şey daha zor görünür. Araştırmalar, yetersiz uykunun duygu düzenlemeyi zayıflattığını ve kaygıya yatkınlığı artırdığını gösteriyor. Uyku bir lüks değil, ruh sağlığının temel taşlarından biridir.',
      'Her gün aynı saatte kalkmaya çalışın, hafta sonu da dahil. Biyolojik saatiniz en çok uyanma saatine göre ayarlanır. Geç yattığınız bir gecenin ardından bile aynı saatte kalkmak, bir sonraki gece uykuya dalmayı kolaylaştırır.',
      'Yatak odasını yalnız uyku için kullanın. Yatakta telefon karıştırmak, iş yazışmalarına bakmak beyne "burası uyanık kalınacak yer" mesajı verir. Yirmi dakika içinde uyuyamadıysanız kalkın, loş ışıkta sakin bir şey yapın, uykunuz gelince geri dönün.',
      'Öğleden sonra kafeini azaltın, akşam yemeğini hafif tutun ve yatmadan bir saat önce ekranları kapatın. Haftalarca süren uykusuzluk yaşıyorsanız, uykusuzluk için Bilişsel Davranışçı Terapi (BDT-U) ilaçsız ve kalıcı bir çözüm sunabilir.',
    ].join('\n\n'),
    author: { name: 'Uzm. Psk. Mehmet Demir', title: 'Uzman Psikolog' },
    color: [99, 102, 241],
  },
  {
    category: 'depresyon',
    tags: ['psikoloji', 'farkındalık'],
    title: 'Depresyon ile Üzüntü Arasındaki Fark',
    excerpt:
      'Üzüntü hayatın doğal bir parçasıdır ve zamanla hafifler. Depresyon ise haftalarca süren, enerjiyi, uykuyu ve zevk alma yetisini etkileyen bir tablodur. Farkı bilmek, yardım istemeyi kolaylaştırır.',
    content: [
      'Bir kayıp, bir hayal kırıklığı ya da zor bir dönem sonrasında üzgün hissetmek sağlıklı bir tepkidir. Üzüntü genellikle bir olaya bağlıdır, dalgalar hâlinde gelir ve güzel anlar onu kısa süreliğine de olsa hafifletebilir.',
      'Depresyonda ise çökkün ruh hali ya da eskiden keyif alınan şeylere karşı ilgisizlik en az iki hafta boyunca, günün büyük bölümünde sürer. Buna uyku ve iştah değişiklikleri, yorgunluk, odaklanma güçlüğü, değersizlik ya da suçluluk duyguları eşlik edebilir.',
      'Depresyon bir karakter zayıflığı değildir ve "kendini toparla" telkinleriyle geçmez. Psikoterapi, gerektiğinde ilaç tedavisiyle birlikte, depresyonun tedavisinde etkili yöntemlerdir. Belirtiler tanıdık geliyorsa bir ruh sağlığı uzmanıyla görüşmeyi ertelemeyin.',
      'Kendinize zarar verme düşünceleriniz varsa beklemeyin: 112 Acil Çağrı Merkezi\'ni arayın ya da en yakın acil servise başvurun.',
    ].join('\n\n'),
    author: { name: 'Uzm. Psk. Elif Kaya', title: 'Klinik Psikolog' },
    color: [100, 116, 139],
  },
  {
    category: 'iliski-problemleri',
    tags: ['iletişim', 'sınırlar'],
    title: 'İlişkilerde Sağlıklı Sınır Koymak',
    excerpt:
      'Sınır koymak karşınızdakini uzaklaştırmak değil, ilişkinin ikiniz için de güvenli kalmasını sağlamaktır. "Hayır" demeyi öğrenmek suçluluk değil, öz saygı işidir.',
    content: [
      'Sınırlar, neyin bizim için kabul edilebilir olduğunu belirleyen görünmez çizgilerdir. Sağlıklı sınırları olmayan ilişkilerde kırgınlık birikir, iletişim kopar ve taraflardan biri kendini sürekli tükenmiş hisseder.',
      'İlk adım kendi ihtiyacınızı fark etmektir. Hangi durumlarda gerildiğinizi, hangi isteklere istemeden "evet" dediğinizi not edin. Bu notlar sınırlarınızın nerede olduğunu gösterir.',
      'Sınırı ifade ederken "ben" diliyle konuşun: "Beni hiç dinlemiyorsun" yerine "Konuşurken sözüm kesildiğinde kendimi değersiz hissediyorum, sözümü bitirmeme izin vermeni istiyorum" deyin. Suçlamadan, açık ve sakin bir dille konuşmak karşı tarafın savunmaya geçmesini azaltır.',
      'Sınırlar ilk başta tepki görebilir; bu, yanlış yaptığınız anlamına gelmez. Tutarlı kaldıkça ilişki yeni dengesini bulur. Tekrarlayan çatışmalar varsa çift terapisi, iki tarafın da duyulduğu güvenli bir alan sunar.',
    ].join('\n\n'),
    author: { name: 'Psk. Dan. Zeynep Aksoy', title: 'Aile ve Çift Terapisti' },
    color: [244, 114, 182],
  },
  {
    category: 'panik-atak',
    tags: ['teknikler'],
    title: 'Panik Atak Sırasında Ne Yapmalı?',
    excerpt:
      'Panik atak korkutucudur ama tehlikeli değildir. Atak sırasında bedeninizde neler olduğunu bilmek ve birkaç basit adımı uygulamak, korkunun korkusunu azaltır.',
    content: [
      'Panik atak, birkaç dakika içinde zirveye ulaşan yoğun bir korku dalgasıdır. Çarpıntı, nefes darlığı, baş dönmesi ve "kontrolümü kaybediyorum" hissi sık görülen belirtilerdir. Atak ne kadar yoğun olursa olsun genellikle on dakika içinde hafiflemeye başlar.',
      'Atak sırasında kaçmaya ya da belirtilerle savaşmaya çalışmayın. Kendinize "Bu bir panik atak, geçecek" deyin. Nefes verişinizi uzatın ve ayaklarınızın yere bastığını hissedin.',
      'Ataklar tekrarlıyorsa ve onları yaşamamak için bazı yerlerden kaçınmaya başladıysanız, bir uzmandan destek alma zamanı gelmiştir. Panik bozukluğu tedaviye en iyi yanıt veren durumlardan biridir.',
    ].join('\n\n'),
    author: { name: 'Uzm. Psk. Mehmet Demir', title: 'Uzman Psikolog' },
    color: [245, 158, 11],
    scheduleInDays: 7, // bilinçli: panelde "Planlandı" durumu görünsün, mobilde henüz görünmesin
  },
]

// ---------------------------------------------------------------- HTTP katmanı

const calls = []
let failed = 0

/**
 * Tek bir HTTP çağrısı yapar, sonucu hem ekrana hem rapora yazar.
 * `soft` verilirse başarısızlık sayaca işlenmez (zaten var olan kayıt gibi
 * beklenen durumlar için).
 */
async function call(method, path, { token, body, soft = false, expected = [], label = '' } = {}) {
  const headers = { Accept: 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  let response
  let text = ''
  try {
    // auth uçlarında IP başına dakikada 10 istek var ve sayaç SÜREÇLER ARASI
    // yaşar — script'i arka arkaya iki kez koşturmak 429 üretir. Beklemek
    // doğru davranış: 429'u hata olarak raporlamak sözleşme hatası sanılır.
    for (let attempt = 0; ; attempt += 1) {
      response = await fetch(`${API}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      text = await response.text()
      if (response.status !== 429 || attempt >= 2) break
      console.log(`  … 429 (rate limit) — 62 sn beklenip yeniden denenecek: ${method} ${path}`)
      await new Promise((resolve) => setTimeout(resolve, 62_000))
    }
  } catch (error) {
    const row = { method, path, status: 0, ok: false, label, error: String(error) }
    calls.push(row)
    failed += 1
    console.log(`  ✗ ---  ${method} ${path}  ${label}  → ${error}`)
    return { ok: false, status: 0, data: null, text: String(error) }
  }

  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = null
    }
  }

  // `expected`: sistemin DOĞRU davranışı olan reddetmeler (mükerrer kayıt gibi).
  // Bunlar ikinci koşumun normal hâli; kırmızı yazmak script'i yalancı yapar.
  const tolerated = soft || (data?.code && expected.includes(data.code))
  const ok = response.ok
  calls.push({ method, path, status: response.status, ok: ok || tolerated, label })
  if (!ok && !tolerated) failed += 1

  const mark = ok ? '✓' : tolerated ? '·' : '✗'
  const detail = ok ? label : `${label} ${text.slice(0, 200)}`
  console.log(`  ${mark} ${String(response.status).padEnd(3)} ${method} ${path}  ${detail}`.trimEnd())

  return { ok, status: response.status, data, text }
}

/** Çok parçalı dosya yükleme — `call` yalnız JSON taşıdığı için ayrı. */
async function upload(path, { token, bytes, fileName, contentType, label = '' }) {
  const form = new FormData()
  form.append('file', new Blob([bytes], { type: contentType }), fileName)
  let response
  let text = ''
  try {
    response = await fetch(`${API}${path}`, {
      method: 'POST',
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
      body: form,
    })
    text = await response.text()
  } catch (error) {
    calls.push({ method: 'POST', path, status: 0, ok: false, label, error: String(error) })
    failed += 1
    console.log(`  ✗ ---  POST ${path}  ${label}  → ${error}`)
    return { ok: false, status: 0, data: null }
  }

  calls.push({ method: 'POST', path, status: response.status, ok: response.ok, label })
  if (!response.ok) failed += 1
  const mark = response.ok ? '✓' : '✗'
  console.log(`  ${mark} ${String(response.status).padEnd(3)} POST ${path}  ${response.ok ? label : `${label} ${text.slice(0, 200)}`}`)
  return { ok: response.ok, status: response.status, data: text ? JSON.parse(text) : null }
}

// Kapak görseli için düz renkli 16:9 PNG üretir. Dışarıdan görsel indirmek
// seed'i ağ erişimine bağlardı; API ise yalnız gerçek dosya imzasını
// (magic bytes) kabul ediyor — bu yüzden geçerli bir PNG şart.
const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n
  for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})

function crc32(buffer) {
  let crc = 0xffffffff
  for (const byte of buffer) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

function pngChunk(type, data) {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([length, body, crc])
}

function solidPng(width, height, [r, g, b]) {
  const header = Buffer.alloc(13)
  header.writeUInt32BE(width, 0)
  header.writeUInt32BE(height, 4)
  header[8] = 8 // bit derinliği
  header[9] = 2 // truecolor RGB
  const row = Buffer.alloc(1 + width * 3)
  for (let x = 0; x < width; x += 1) row.set([r, g, b], 1 + x * 3)
  const raw = Buffer.concat(Array.from({ length: height }, () => row))
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(raw)),
    pngChunk('IEND', Buffer.alloc(0)),
  ])
}

function section(title) {
  console.log(`\n─── ${title}`)
}

function die(message) {
  console.error(`\nDURDU: ${message}`)
  process.exit(1)
}

// Auth uçlarında IP başına dakikada 10 istek sınırı var. Sınıra dayanırsak
// beklemek, testi 429 yüzünden kırmızı göstermekten iyidir.
let authCalls = 0
async function authCall(path, body, options = {}) {
  if (authCalls > 0 && authCalls % 8 === 0) {
    console.log('  … auth rate limit siperi: 62 sn bekleniyor')
    await new Promise((resolve) => setTimeout(resolve, 62_000))
  }
  authCalls += 1
  return call('POST', path, { body, ...options })
}

// ---------------------------------------------------------------------- akış

async function main() {
  console.log(`PsikoAl demo seed — ${API}`)

  const health = await call('GET', '/health')
  if (!health.ok) die('API ayakta değil. Önce: bash scripts/e2e-stack.sh')

  // --- 1. Admin girişi -----------------------------------------------------
  section('1. Admin girişi')
  const adminLogin = await authCall('/auth/login', ADMIN)
  if (!adminLogin.ok) die('Admin girişi başarısız.')
  const adminToken = adminLogin.data.tokens.accessToken

  // --- 2. Kategoriler ------------------------------------------------------
  // Kanonik 17 zaten migration seed'inden geliyor; burada üretilmez, DOĞRULANIR.
  // Kanonik olmayan her satır (E2E artığı, bayat mock-db kopyası) pasifleştirilir
  // — silme ucu yok ve zaten silmek yanlış olurdu: pasifleştirme audit'e düşer.
  section('2. Kategoriler doğrulanıyor + artıklar pasifleştiriliyor')
  const existing = await call('GET', '/admin/categories', { token: adminToken })
  const rows = Array.isArray(existing.data) ? existing.data : (existing.data?.data ?? [])
  const bySlug = new Map(rows.map((c) => [c.slug, c]))

  const missing = CANONICAL_CATEGORIES.filter((slug) => !bySlug.has(slug))
  if (missing.length > 0) {
    console.log(`  ✗ --- migration seed'i eksik: ${missing.join(', ')}`)
    failed += 1
  } else {
    console.log(`  ✓ --- kanonik 17 kategorinin hepsi yerinde`)
  }

  for (const slug of CANONICAL_CATEGORIES) {
    const category = bySlug.get(slug)
    if (category && category.isActive === false) {
      await call('PATCH', `/admin/categories/${category.id}`, {
        token: adminToken,
        label: `${slug} yeniden aktifleştirildi`,
        body: { isActive: true },
      })
    }
  }

  const canonical = new Set(CANONICAL_CATEGORIES)
  const strays = rows.filter((c) => !canonical.has(c.slug) && c.isActive !== false)
  for (const stray of strays) {
    await call('PATCH', `/admin/categories/${stray.id}`, {
      token: adminToken,
      label: `artık pasifleştirildi: ${stray.slug}`,
      body: { isActive: false },
    })
  }
  if (strays.length === 0) console.log('  · --- pasifleştirilecek artık kategori yok')

  // --- 3. Danışan profili --------------------------------------------------
  section('3. Danışan kaydı/girişi + profil')
  const clientRegister = await authCall(
    '/auth/register',
    {
      email: CLIENT.email,
      password: CLIENT.password,
      firstName: CLIENT.firstName,
      lastName: CLIENT.lastName,
      role: 'client',
      termsAccepted: true,
      healthDataConsent: true,
    },
    { soft: true, label: CLIENT.email }
  )

  const clientLogin = clientRegister.ok
    ? clientRegister
    : await authCall('/auth/login', { email: CLIENT.email, password: CLIENT.password })
  if (!clientLogin.ok) die('Danışan ne kaydolabildi ne giriş yapabildi.')
  const clientToken = clientLogin.data.tokens.accessToken
  const clientId = clientLogin.data.user.id

  await call('PATCH', '/auth/me', {
    token: clientToken,
    label: `${CLIENT.firstName} ${CLIENT.lastName}`,
    body: {
      firstName: CLIENT.firstName,
      lastName: CLIENT.lastName,
      phone: '+905551112233',
      city: 'İstanbul',
      shareEmail: false,
      sharePhone: false,
      shareLocation: true,
    },
  })

  // --- 4. Uzmanlar ---------------------------------------------------------
  section('4. Uzman kaydı → profil → admin onayı')
  const experts = []
  for (const expert of EXPERTS) {
    let token
    // Hesap zaten varsa 422 AUTH_EMAIL_ALREADY_REGISTERED gelir; bu ikinci
    // koşumun normal hâli, rapora başarısızlık olarak yazılmaz.
    const register = await authCall(
      '/auth/register',
      {
        email: expert.email,
        password: expert.password,
        firstName: expert.firstName,
        lastName: expert.lastName,
        role: 'expert',
        termsAccepted: true,
        healthDataConsent: true,
      },
      { soft: true, label: expert.email }
    )

    if (register.ok) {
      token = register.data.tokens.accessToken
    } else {
      const login = await authCall('/auth/login', { email: expert.email, password: expert.password })
      if (!login.ok) die(`${expert.email} ne kaydolabildi ne giriş yapabildi.`)
      token = login.data.tokens.accessToken
    }

    let profile = await call('POST', '/experts/profile', {
      token,
      label: `${expert.firstName} ${expert.lastName}`,
      body: expert.profile,
      soft: true,
    })

    if (!profile.ok) {
      // Profil zaten varsa POST reddeder — PATCH ile güncelle.
      profile = await call('PATCH', '/experts/profile', {
        token,
        label: `${expert.firstName} ${expert.lastName} (güncelleme)`,
        body: expert.profile,
      })
      if (!profile.ok) die(`${expert.email} profili oluşturulamadı.`)
    }

    const expertId = profile.data.id
    await call('POST', `/admin/experts/${expertId}/approve`, { token: adminToken, label: 'onay', soft: true })
    await call('POST', `/admin/experts/${expertId}/verify`, {
      token: adminToken,
      label: 'rozet',
      body: { isVerified: true },
    })

    experts.push({ ...expert, token, id: expertId })
  }

  // --- 4b. Uzman bakiyesi --------------------------------------------------
  // Teklif göndermek TL ücreti (ya da ücretsiz hak) harcar. Demo uzmanları,
  // admin cüzdan panelinin hediye ucundan DEMO_EXPERT_BALANCE_KURUS'a
  // tamamlanır (yalnız eksik kadar; script tekrar koşunca bakiye şişmez).
  section('4b. Admin demo uzmanlarına bakiye hediye eder')
  for (const expert of experts) {
    const wallet = await call('GET', `/admin/wallet/experts/${expert.id}`, {
      token: adminToken,
      label: `${expert.firstName} bakiye`,
    })
    const missing = DEMO_EXPERT_BALANCE_KURUS - (wallet.data?.balanceKurus ?? 0)
    if (!wallet.ok || missing <= 0) continue
    await call('POST', `/admin/wallet/experts/${expert.id}/gift`, {
      token: adminToken,
      label: `${expert.firstName} +${missing / 100} TL`,
      body: { amountKurus: missing, freeRights: 0, reason: 'Demo verisi: başlangıç bakiyesi' },
    })
  }

  // --- 5. İlanlar ----------------------------------------------------------
  // LISTING_MAX_ACTIVE varsayılanı 3; dördüncü ilan bu sipere takılır. Ayarı
  // admin ucundan gevşetmek hem 4. ilanı mümkün kılar hem system-settings
  // ucunun gerçekten davranışı değiştirdiğini kanıtlar.
  section('5. Sistem ayarı gevşetilir → danışan ilan açar → admin onaylar')
  await call('PATCH', '/admin/system-settings/LISTING_MAX_ACTIVE', {
    token: adminToken,
    label: '3 → 5',
    body: { value: '5' },
  })

  const mine = await call('GET', '/listings/my', { token: clientToken, label: 'mevcut ilanlar' })
  const mineRows = mine.data?.data ?? mine.data ?? []
  const existingTitles = new Set(Array.isArray(mineRows) ? mineRows.map((l) => l.title) : [])

  const listings = []
  for (const listing of LISTINGS) {
    if (existingTitles.has(listing.title)) {
      console.log(`  · --- ilan zaten var: ${listing.title.slice(0, 40)}`)
      const already = mineRows.find((l) => l.title === listing.title)
      listings.push({ ...listing, id: already.id })
      continue
    }
    const created = await call('POST', '/listings', {
      token: clientToken,
      label: listing.title.slice(0, 40),
      body: {
        title: listing.title,
        description: listing.description,
        specialization: listing.specialization,
        budgetMin: listing.budgetMin,
        budgetMax: listing.budgetMax,
        preferredSessionType: listing.preferredSessionType,
        city: listing.city,
        contactConsent: true,
        assessmentResultId: null,
      },
    })
    if (!created.ok) continue

    if (listing.approve) {
      await call('POST', `/admin/listings/${created.data.id}/approve`, { token: adminToken, label: 'onay' })
    } else {
      console.log('  · --- bilinçli olarak PENDING_APPROVAL bırakıldı')
    }
    listings.push({ ...listing, id: created.data.id })
  }

  const openListings = listings.filter((l) => l.approve)
  if (openListings.length < 3) die('Yeterli OPEN ilan yok, teklif adımı anlamsız olur.')

  // --- 6. Teklifler --------------------------------------------------------
  section('6. Uzmanlar teklif verir')
  const offers = []
  const offerPlan = [
    { expert: 0, listing: 0, price: 1200, sessionType: 'online', title: 'Haftalık BDT paketi', description: 'İlk seans değerlendirme, sonrasında haftalık 50 dakikalık online seanslar. Ara ödevlerle ilerleriz.' },
    { expert: 1, listing: 0, price: 1350, sessionType: 'online', title: 'Panik atak odaklı program', description: 'Nefes ve maruz bırakma teknikleriyle sekiz haftalık yapılandırılmış program.' },
    { expert: 1, listing: 1, price: 1600, sessionType: 'yüz_yüze_online', title: 'Tükenmişlik programı', description: 'Kariyer kararları ve tükenmişlik için şema terapi temelli süreç.' },
    { expert: 2, listing: 2, price: 2200, sessionType: 'yüz_yüze', title: 'Çift terapisi süreci', description: 'İki haftada bir 80 dakikalık çift seansı, aralarda iletişim ödevleri.' },
  ]

  for (const plan of offerPlan) {
    const expert = experts[plan.expert]
    const listing = openListings[plan.listing]
    if (!expert || !listing) continue

    // Ücret ilanın kalitesine, yaşına ve teklif sayısına göre değişir; gönderim
    // anlık ücreti expectedFeeKurus ile onaylamak zorunda (aksi 409 OFFER_FEE_CHANGED).
    const quote = await call('GET', `/offers/quote?listingId=${listing.id}`, {
      token: expert.token,
      label: `${expert.firstName} ücret`,
    })
    if (!quote.ok) continue

    const offer = await call('POST', '/offers', {
      token: expert.token,
      label: `${expert.firstName} → ${listing.title.slice(0, 30)}`,
      expected: ['OFFER_ALREADY_EXISTS', 'OFFER_LISTING_NOT_OPEN'],
      body: {
        listingId: listing.id,
        expectedFeeKurus: quote.data.data.feeKurus,
        title: plan.title,
        price: plan.price,
        sessionType: plan.sessionType,
        description: plan.description,
      },
    })
    if (offer.ok) offers.push({ ...plan, id: offer.data.id, expert, listing })
  }

  // --- 7. Eşleşti işaretleme ----------------------------------------------
  section('7. Danışan bir teklif sahibiyle "eşleşti" işaretler')
  // Önceki koşum zinciri tamamladıysa yeni eşleşme kurulmaz; mevcut olan bulunur.
  const priorMatches = await call('GET', '/matches', { token: clientToken, label: 'mevcut eşleşmeler' })
  const priorRows = priorMatches.data?.data ?? priorMatches.data ?? []
  const alreadyMatched = Array.isArray(priorRows) && priorRows.length > 0

  const accepted = offers.find((o) => o.listing.id === openListings[0].id)
  let matchId = alreadyMatched ? priorRows[0].id : null
  if (alreadyMatched) {
    console.log(`  · --- eşleşme zaten var: ${matchId} (${priorRows[0].status})`)
  } else if (!accepted) {
    console.log('  ✗ eşleşilecek teklif bulunamadı')
    failed += 1
  } else {
    const match = await call('POST', `/listings/${accepted.listing.id}/match`, {
      token: clientToken,
      label: `${accepted.expert.firstName} ile eşleşti`,
      body: { offerId: accepted.id },
    })
    if (match.ok) matchId = match.data.matchId ?? null
  }

  // --- 8. Yorum → moderasyon -----------------------------------------------
  // Eşleşme sonlandırılmaz; değerlendirme eşleşir eşleşmez açıktır.
  section('8. Danışan yorum yazar → admin onayı')
  if (!matchId) {
    console.log('  ✗ eşleşme yok, yorum zinciri atlandı')
    failed += 1
  } else {
    const reviewedExpert = experts.find((e) => e.id === accepted?.expert.id) ?? experts[0]
    const review = await call('POST', `/experts/${reviewedExpert.id}/reviews`, {
      token: clientToken,
      label: '5 yıldız',
      expected: ['REVIEW_ALREADY_EXISTS'],
      body: {
        matchId,
        rating: 5,
        comment:
          'İlk seanstan itibaren ne yaşadığımı anladığını hissettirdi. Verdiği ödevler günlük hayatta gerçekten işe yaradı, atakların sıklığı belirgin şekilde azaldı.',
        sessionType: 'online',
      },
    })

    if (review.ok) {
      const pending = await call('GET', '/admin/reviews?status=pending', { token: adminToken })
      const rows = pending.data?.data ?? pending.data ?? []
      const target = Array.isArray(rows) ? rows.find((r) => r.id === review.data.id) ?? rows[0] : null
      if (target) {
        await call('POST', `/admin/reviews/${target.id}/approve`, { token: adminToken, label: 'yorum onayı' })
      } else {
        console.log('  ✗ onay kuyruğunda yorum görünmedi')
        failed += 1
      }
    }
  }

  // --- 9. Bildirimler ------------------------------------------------------
  section('9. Admin bildirim gönderir')
  await call('POST', '/admin/notifications/send', {
    token: adminToken,
    label: 'tek kullanıcı',
    body: {
      targetType: 'user',
      userId: clientId,
      role: null,
      city: null,
      specialization: null,
      title: 'İlanınız yayında',
      body: 'Panik atak ilanınız onaylandı ve uzmanlara açıldı. Gelen teklifleri Teklifler sekmesinden görebilirsiniz.',
    },
  })
  await call('POST', '/admin/notifications/send', {
    token: adminToken,
    label: 'danışan segmenti',
    body: {
      targetType: 'segment',
      userId: null,
      role: 'client',
      city: null,
      specialization: null,
      title: 'Yeni testler eklendi',
      body: 'Kaygı ve depresyon tarama testleri güncellendi. Testler sekmesinden ücretsiz çözebilirsiniz.',
    },
  })

  // --- 10. Blog yazıları ---------------------------------------------------
  // Oluştur (taslak) → kapak → yayınla ya da planla. İkinci koşumda yazı zaten
  // admin listesinde durur; o zaman yalnız eksik adımlar tamamlanır.
  section('10. Admin blog yazılarını yayınlar')
  const existingBlogs = await call('GET', '/admin/blogs', { token: adminToken, label: 'mevcut yazılar' })
  const blogsByTitle = new Map(
    (Array.isArray(existingBlogs.data) ? existingBlogs.data : []).map((b) => [b.title, b]),
  )
  let firstBlogSlug = null

  for (const post of BLOGS) {
    let blog = blogsByTitle.get(post.title)
    if (blog) {
      console.log(`  · --- zaten var: ${post.title}`)
    } else {
      const created = await call('POST', '/admin/blogs', {
        token: adminToken,
        label: post.title,
        body: {
          title: post.title,
          slug: null,
          excerpt: post.excerpt,
          content: post.content,
          categoryId: bySlug.get(post.category)?.id ?? null,
          tags: post.tags,
          authorName: post.author.name,
          authorTitle: post.author.title,
        },
      })
      if (!created.ok) continue
      blog = { ...created.data, hasCover: false }
    }

    if (!blog.hasCover) {
      await upload(`/admin/blogs/${blog.id}/cover`, {
        token: adminToken,
        bytes: solidPng(320, 180, post.color),
        fileName: 'kapak.png',
        contentType: 'image/png',
        label: 'kapak',
      })
    }

    if (post.scheduleInDays) {
      if (blog.status !== 'scheduled') {
        const publishAt = new Date(Date.now() + post.scheduleInDays * 86_400_000).toISOString()
        await call('POST', `/admin/blogs/${blog.id}/schedule`, {
          token: adminToken,
          label: `${post.scheduleInDays} gün sonraya planlandı`,
          body: { publishAt },
        })
      }
      continue
    }

    if (blog.status !== 'published') {
      await call('POST', `/admin/blogs/${blog.id}/publish`, { token: adminToken, label: 'yayınlandı' })
    }
    firstBlogSlug ??= blog.slug
  }

  // --- 11. Mobilin gördüğü uçlar ------------------------------------------
  section('11. Mobil uçları doğrula (danışan gözüyle)')
  await call('GET', '/categories', { token: clientToken, label: 'keşfet' })
  await call('GET', '/listings/my', { token: clientToken, label: 'ilanlarım' })
  await call('GET', '/offers/my', { token: clientToken, label: 'tekliflerim' })
  await call('GET', '/matches', { token: clientToken, label: 'eşleşmelerim' })
  await call('GET', '/notifications', { token: clientToken, label: 'bildirimler' })
  await call('GET', '/assessment', { token: clientToken, label: 'testler' })
  await call('GET', '/assessment/results/my', { token: clientToken, label: 'test sonuçlarım' })
  await call('GET', '/auth/me', { token: clientToken, label: 'profilim' })
  if (experts[0]) {
    await call('GET', `/experts/${experts[0].id}`, { token: clientToken, label: 'uzman detayı' })
    await call('GET', `/experts/${experts[0].id}/reviews`, { token: clientToken, label: 'uzman yorumları' })
  }
  await call('GET', '/categories/anksiyete', { token: clientToken, label: 'kategori detayı' })
  await call('GET', '/blogs?limit=3', { token: clientToken, label: 'ana sayfa blog karuseli' })
  await call('GET', '/blogs?category=anksiyete', { token: clientToken, label: 'kategori sayfası blogları' })
  await call('GET', '/blogs', { label: 'blog akışı (oturumsuz)' })
  if (firstBlogSlug) {
    await call('GET', `/blogs/${firstBlogSlug}`, { token: clientToken, label: 'blog detayı' })
    // Beğeni bir toggle: script tekrar koşunca beğeniyi geri almasın diye
    // sonuç "beğenmedi" ise bir kez daha basılır.
    const like = await call('POST', `/blogs/${firstBlogSlug}/like`, { token: clientToken, label: 'beğeni', body: {} })
    if (like.ok && like.data?.liked === false) {
      await call('POST', `/blogs/${firstBlogSlug}/like`, { token: clientToken, label: 'beğeni (geri)', body: {} })
    }
  }

  section('12. Mobil uçları doğrula (uzman gözüyle)')
  if (experts[0]) {
    await call('GET', '/listings', { token: experts[0].token, label: 'ilan akışı' })
    await call('GET', '/offers/my', { token: experts[0].token, label: 'verdiğim teklifler' })
    await call('GET', '/matches', { token: experts[0].token, label: 'eşleşmelerim' })
  }

  // --- Özet ----------------------------------------------------------------
  const total = calls.length
  const bad = calls.filter((c) => !c.ok)
  console.log(`\n${'═'.repeat(70)}`)
  console.log(`Toplam çağrı: ${total}   Başarısız: ${bad.length}`)
  if (bad.length > 0) {
    console.log('\nBaşarısız çağrılar:')
    for (const c of bad) console.log(`  ${String(c.status).padEnd(3)} ${c.method} ${c.path}  ${c.label}`)
  }
  process.exit(failed > 0 ? 1 : 0)
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
