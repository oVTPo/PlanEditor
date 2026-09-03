# Quản lý phiên bản PA-S

## Thiết lập một lần

Chép toàn bộ nội dung gói vào thư mục gốc repository, sau đó:

```bash
chmod +x release.sh
git add .
git commit -m "build: thêm quy trình phát hành tự động"
git push
```

## Phát hành

Commit phần tính năng trước:

```bash
git add .
git commit -m "feat(measure): thêm công cụ đo"
git push
```

Tạo bản phát hành:

```bash
./release.sh patch
```

- `patch`: sửa lỗi, `0.1.0 → 0.1.1`.
- `minor`: thêm tính năng, `0.1.1 → 0.2.0`.
- `major`: thay đổi lớn, `0.9.0 → 1.0.0`.
- `beta`: `0.1.0 → 0.2.0-beta.1`; chạy lại thành `beta.2`.
- Bản cụ thể: `./release.sh 0.4.0-beta.1`.

Script tự tăng version, cập nhật changelog, build, commit, tạo tag và push.
GitHub Actions nhận tag, publish macOS ARM64, đóng ZIP và tạo GitHub Release.

## Quy tắc commit

- `feat:` tính năng mới.
- `fix:` sửa lỗi.
- `ui:` giao diện.
- `perf:` hiệu năng.
- `docs:` tài liệu.
- `build:` build/phát hành.
- `refactor:` chỉnh cấu trúc.

Ví dụ:

```text
feat(measure): cho phép đổi đơn vị đo
fix(print): sửa bản đồ bị đen khi xuất DOCX
ui(symbols): thu gọn tiêu đề nhóm ký hiệu
```

Nếu gói ZIP lớn hơn giới hạn upload thông thường, workflow tự chia thành
các phần nhỏ. Ghép lại trên macOS bằng:

```bash
cat PA-S-*.zip.part-* > PA-S-version-macOS-arm64.zip
```
