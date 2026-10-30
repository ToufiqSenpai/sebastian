# PRD — Sebastian

Asisten development pribadi, self-hosted, untuk mempercepat iterasi proyek yang saya kerjakan sendiri (publik maupun privat, nanti termasuk freelance dan tim kecil).

Versi: 0.1 (draf untuk didiskusikan)
Tanggal: 29 Oktober 2026

---

## 1. Masalah

1. Ketika saya membuat spec, saya tidak menundanya dulu di sebuah sistem ticket, namun langsung mengimplementasikannya menggunakan agent. Seharusnya spec itu ditampung dulu dalam ticket.
2. Saya tidak bisa tag agent bot di issue dan juga review, sehingga saya tidak bisa minta pendapat agent.
3. Saya ingin fitur seperti Claude Code Agent, tetapi itu berbayar dan berjalan langsung di GitHub runner sehingga menghabiskan kuota. Saya inginnya self-hosted di sebuah sandbox dan mendukung model apapun.
4. Saya ingin membuat workflow implement feature yang sesuai dengan keinginan saya.

## 2. Tujuan

- Mempercepat lifecycle saya ketika implement aplikasi, sehingga saya hanya fokus ke pembuatan spec, dan review code saja.
- Mempermudah saya menanyakan hal tentang project, sehingga saya hanya membuka GitHub saja. Tidak perlu membuka code editor untuk menanyakan issue atau review PR.

### Non-tujuan (v1)

- Multi-user, multi-tenant, dashboard web, antarmuka chat.
- Auto-merge tanpa perintah eksplisit, auto-approve PR, atau publish rilis.
- Otomasi CVE/advisory.
- Menangani lebih dari satu repo sekaligus.
- Menggantikan saya dalam menilai apakah sebuah fitur layak dibuat.

## 3. Permukaan produk

Sebastian hanya punya satu permukaan: pipeline otomatis. Tidak ada mode interaktif, chat, atau CLI untuk bekerja bersama agent.

### 3.1 Alur

Satu alur untuk bug dan fitur, dari issue sampai PR. Ujungnya PR, bukan merge. Label `bug` atau `feature` menentukan syarat Validasi.

1. **Issue masuk** — bug atau fitur masuk lewat template. Ini satu-satunya titik masuk.
2. **Validasi** — Sebastian memeriksa reproduksi, kelengkapan template, dan bukti, dengan syarat per label; setiap penutupan mencatat alasannya di issue:
   - **`feature`** — spec lolos → **Implementasi**. Spec tidak sesuai → **Ditutup** (terminal).
   - **`bug`** — bug jelas (bisa direproduksi, ada bukti) → **Implementasi**. Tidak bisa direproduksi atau tanpa bukti → **Ditutup** (terminal).
   - **Ragu atau berisiko**, label apa pun → **needs-triage**.
3. **needs-triage** — menunggu manusia. Hanya maintainer yang mengambil issue lalu memindahkannya ke **ready-for-agent**; selama di sini tidak ada kerja agent.
4. **ready-for-agent** — gerbang terakhir. Status ini hanya dipegang maintainer; agent tidak pernah memindahkan issue ke sini sendiri. Setelah maintainer setuju → **Implementasi**.
5. **Implementasi** — agent bekerja di branch + test. Lulus → **Pull request**.
6. **Pull request** — ujung alur. PR tidak pernah di-merge tanpa perintah eksplisit (bagian 4).

### 3.2 Di luar cakupan

Refactor, dependency update, perbaikan style, dan pekerjaan yang ingin saya langsung mulai, tidak dibuatkan issue dan tidak dijalankan Sebastian. Semuanya dikerjakan langsung di harness agent seperti OpenCode.

## 4. Aturan keras

- Tidak pernah approve PR yang ia tulis sendiri; review agent bukan approval.
- Merge hanya dieksekusi setelah perintah eksplisit (`merge`), tidak pernah atas inisiatif sendiri.
- Tidak pernah menutup PR atau issue orang lain tanpa alasan yang tercatat.
- Tidak pernah menulis detail security di kanal publik.
- Tidak pernah menghapus/menyunting komentar orang lain.
- Setiap perubahan kode lewat branch + PR.
- `main` di origin dilindungi: tidak ada push langsung; semua perubahan lewat PR.
- `main` lokal adalah mirror read-only dari `origin/main`; agent selalu mulai dari `main` lokal yang bersih.
- Batas biaya per run dan per hari; lewat batas → berhenti, bukan lanjut.

## 5. Keputusan

| # | Keputusan | Catatan |
|---|---|---|
| 1 | Pemicu: webhook + tunnel. | Respons cepat; interaksi tetap lewat issue/komentar GitHub. PC dan tunnel harus hidup 24/7; signature webhook wajib diverifikasi. |
| 2 | Runtime agent: framework agentic sendiri, bukan CLI headless. | Lebih bebas; framework spesifik belum dipilih. |
| 3 | Lokasi: PC lokal untuk v1. | Tanpa biaya VPS; bergantung pada PC yang hidup. |
| 4 | State: label di issue saja, tanpa database. | Cukup untuk satu repo; label menyimpan state sekarang, bukan riwayat. |
| 5 | Triage bug jika menyentuh salah satu pemicu di bawah; di luar itu auto-fix. | — |
| 6 | Komentar: bahasa mengikuti issue, nada maintainer biasa. | Singkat, langsung, tanpa basa-basi. |
| 7 | Base implementasi: `main` lokal (mirror `origin/main`). | Sinkron lewat push webhook + startup sync; ff-only, tanpa auto-resolve konflik. Commit lokal di `main` ditolak sebelum agent mulai. |

Pemicu triage bug (#5):

- **Security** — auth, izin, potensi kebocoran data.
- **Business logic** — harga, billing, aturan bisnis, transisi state.
- **Data** — migrasi, penghapusan, potensi hilang data, PII.
- **Kontrak publik** — API, skema, format config yang dipakai pihak lain.
- **Infrastruktur** — CI/CD, deploy, secret.
- **Lintas modul** — perbaikan menyentuh beberapa modul atau butuh keputusan desain.
- **Intermiten** — bug muncul kadang-kadang; tidak selalu bisa direproduksi.
- **Ambigu** — bug bisa direproduksi, tetapi perilaku yang diharapkan tidak jelas.
- **False positive** — laporan yang tampak bukan bug (perilaku sudah sesuai atau masalah di lingkungan pelapor); jangan ditutup otomatis.

## 10. Risiko

- **Prompt injection** dari isi issue publik → batasi izin token, jangan pernah memberi akses ke secret.
- **Kebocoran security** lewat komentar, nama branch, pesan commit, atau log → aturan "diam" harus ada sejak run pertama.
- **Banjir triage** kalau ambang terlalu longgar
