# Intent: Chat-first work board untuk Sebastian

Author: Muhammad Taufiqurrahman. Status: draft. Tanggal: 31 Oktober 2026.

Konteks: ini arah baru Sebastian setelah percakapan tentang AI-native SDLC. Intent ini menggantikan bagian PRD 0.1 yang menjadikan GitHub Issue sebagai intake dan penyimpanan state, serta menggantikan draf intent sebelumnya. GitHub tetap dipakai untuk kode: branch, commit, PR, dan merge.

## Masalah

Sebastian sekarang menggantungkan seluruh alur ke GitHub Issue: intake, validasi spec, dan state lewat label. Itu cukup untuk eksperimen pertama, tetapi tidak cocok dengan cara saya bekerja.

- Planning, implementation, dan review terjadi di satu sesi harness yang sama. Tidak ada artifact handoff dan tidak ada gate; saya yang menjadi antrean.
- GitHub Issue bukan papan kerja. Label hanya menyimpan status sekarang; tidak ada tipe pekerjaan, riwayat, atau spec yang terstruktur.
- Saya tidak punya satu tempat untuk semua jenis pekerjaan: spec, bug, refactor, chore, enhance, dan security.
- Komentar issue adalah tempat artifact yang buruk: sulit dibaca mesin, mudah diedit, dan tidak terhubung ke chat, run, atau PR.
- Pipeline berhenti di worktree + log. Belum ada commit, PR, review, atau merge, jadi loop-nya belum tertutup.

## Hasil yang diinginkan

Satu sistem kerja chat-first yang memakai kanban sebagai papan kerja untuk semua ticket.

```text
Chat ──create──▶ Ticket
                             │
                             ▼
Kanban: Not Started → In Progress → In Review → Done
                             │
                  type=spec? ─┴─ yes → badge "Spec Review" di Not Started
                                       → approve → In Progress
```

- **Chat adalah satu-satunya pintu masuk.** Saya brainstorming dengan Sebastian lewat chat. Sebastian bisa membuat ticket dari percakapan.
- **Satu kanban untuk semua tipe.** Kolom: Not Started, In Progress, In Review, Done. Tidak ada board terpisah per tipe.
- **Ticket minimal.** Hanya `type`, `status`, `title`, dan `body` (plus id dan timestamp internal). 
- **Tipe ticket:** `spec`, `bug`, `refactor`, `chore`, `enhance`, `security`.
- **Ticket baru masuk Not Started.**
- **`type=spec` punya spec.** `body` berisi spec hasil generate. Spec wajib direview sebelum implementation; review tampil sebagai badge **Spec Review** di Not Started, lalu setelah approve pindah ke In Progress.
- **Tipe lain tidak punya spec gate.** Begitu dimulai, langsung masuk In Progress dan diimplementasikan.
- **Build** dijalankan agent, commit, push, dan membuka PR. Sebagian dikerjakan host.
- **In Review** mencakup review agent dan human review PR. Merge hanya lewat aksi eksplisit.
- **Done** berarti sudah di-merge atau ditutup.
- **State bertahan setelah restart.** Ticket, chat, run, dan riwayat status disimpan di database.

Ukuran berhasil:

- Cycle time per tipe ticket turun dibanding alur OpenCode + GitHub Issue sekarang.
- Human minutes per ticket turun; saya hanya muncul saat brainstorming, review spec (untuk `type=spec`), dan merge.
- Spec revision setelah approve rendah.
- Sebagian besar perubahan lolos verifikasi build/test/lint sebelum sampai ke saya.

## Pengguna dan sistem yang terpengaruh

- Pengguna: saya sendiri, sebagai solo developer dan maintainer.
- Sistem: chat UI streaming; ticket store + database; papan kanban; agent roles (Chat, Spec, Build, Review); JobQueue; Git dan worktree; GitHub App untuk PR dan merge; provider model; filesystem lokal.
- Sumber state baru: database ticket. GitHub Issue dan label tidak lagi menjadi sumber state. Intake webhook issue dipensiunkan; webhook PR tetap ada untuk status PR.

## Batasan

- Self-hosted di PC yang sama; satu instance melayani satu repo di v1.
- Single-user. Tidak ada multi-tenant, signup, atau role management. Board dan API tidak boleh bisa ditulis publik; default hanya localhost, atau di belakang auth reverse proxy kalau di-tunnel.
- Chat adalah satu-satunya surface pembuatan ticket. Tidak ada form atau tombol New Ticket.
- Ticket model minimal: `type`, `status`, `body`; id dan timestamp internal.
- Tipe fixed: `spec`, `bug`, `refactor`, `chore`, `enhance`, `security`.
- Status fixed: `not_started`, `in_progress`, `in_review`, `done`. Tidak ada custom column.
- Spec adalah Markdown di database, bukan file dan tidak di-commit ke repo.
- Spec review adalah gate manusia dan harus selesai sebelum implementation. Review tampil sebagai badge di Not Started, bukan kolom baru.
- Spec revision belum diputuskan; default v1 adalah regenerate lewat chat. Belum ada edit langsung.
- Satu kanban untuk semua tipe; filter by type adalah fitur UI, bukan board terpisah.
- Chat streaming realtime; bukan async turn-based.
- Link ke chat, run, dan PR disimpan di tabel terpisah, bukan field ticket.
- Blocked adalah badge dari run terakhir, bukan field ticket atau kolom kanban.
- GitHub hanya untuk kode: branch, commit, PR, review komentar, dan merge.
- Hard rules tetap berlaku: tidak ada auto-merge, agent tidak approve karyanya sendiri, merge hanya atas aksi eksplisit, budget per run/hari, dan detail security tidak ditulis di kanal publik.
- Verifikasi build/test/lint dijalankan host dan hasilnya disimpan sebagai evidence, bukan hanya klaim agent.
- Non-goal PRD 0.1 tentang dashboard web direvisi: kanban sekarang in-scope; dashboard analytics tetap out.
- Keputusan PRD 0.1 #4 (state di label, tanpa database) digantikan oleh database ticket.
- Scope v1 tidak mencakup: chat multi-sesi, multi-repo, deploy/release automation, agent evals, board terpisah per tipe, edit spec langsung, multi-user.

## Pertanyaan terbuka

1. **Spec revision**: belum diputuskan. Default v1: regenerate lewat chat; edit langsung ditunda sampai ada kebutuhan nyata.
2. **Promosi tipe**: apakah ticket non-spec boleh berubah menjadi `type=spec` di tengah jalan? Belum diputuskan.
3. **Spec untuk tipe lain**: apakah bug atau security yang kompleks boleh punya spec, atau harus membuat ticket `type=spec` terpisah? Belum diputuskan.
4. **Urutan generate spec**: chat membuat spec lalu ticket, atau ticket dulu lalu generate spec? Yang tetap: spec ticket tidak masuk In Progress sebelum spec direview.
5. **Chat session**: satu chat terus-menerus, atau beberapa sesi? Belum diputuskan.
6. **PRD**: tulis ulang PRD 0.1 menjadi 0.2 setelah intent ini disetujui, atau biarkan berdampingan? Rekomendasi: tulis ulang.

## Referensi

- Anthropic, [The AI-native SDLC playbook](https://claude.com/blog/the-ai-native-sdlc-playbook), Agustus 2026.
