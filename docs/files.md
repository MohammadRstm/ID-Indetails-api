# Files

Photos and videos: attachments on messages and work entries, profile photos and the company logo. A file is uploaded first, and its id is then attached to what it belongs to.

## Endpoints

| Endpoint | Who | What |
|---|---|---|
| `POST /api/files` | Signed in | Multipart field `file`. `201` with `{ id, name, mimeType, kind, sizeBytes, createdAt }`. Its own rate limit (`RateLimiting:UploadPermitsPerMinute`, 30 a minute) |
| `GET /api/files/{id}` | Signed in, see below | The content, shown inline, with `X-Content-Type-Options: nosniff`. Videos can be read in ranges |
| `PUT /api/me/photo`, `DELETE /api/me/photo` | Signed in | `{ fileId }`: the caller's profile photo, from an upload of their own. Returns the signed-in user |
| `PUT /api/employees/{id}/photo`, `DELETE …` | `employee:manage` | `{ fileId }`: someone's profile photo, from the caller's upload. Other domains read as `404`; a super admin's needs `employee:invite_super_admin` |
| `PUT /api/company/logo`, `DELETE …` | `company_settings:manage` | `{ fileId }`: the company logo, at any time, before the rest of the settings too |
| `GET /api/company/branding` | Anyone | `{ name, hasLogo }` for the sign-in page; `name` is null until the settings are saved |
| `GET /api/company/logo` | Anyone | The logo's image, or `404` |

Attachments go with what they are attached to: `attachmentIds` (up to 10) when sending a message ([messaging.md](messaging.md)), and `attachmentFileId` when logging or editing a work entry ([design.md](design.md)). Photo ids come back as `photoFileId` on the signed-in user, employees, chat contacts and the Employees page, and as `logoFileId` on the company settings.

## Rules

`Business/Files/FileRules.cs`, `Services/Files/`:

- **Types.** Only photos (JPEG, PNG, WebP, HEIC, up to 10 MB) and videos (MP4, MOV, WebM, up to 100 MB). What a file is comes from its first bytes, never from its name or the type the browser sends (`422 file_type_not_allowed`, `422 file_too_large`). A profile photo or logo must be a photo.
- **Names** are only for display: path parts and unusual characters are dropped, up to 100 characters. The file is stored under a generated key (`yyyy/MM/{guid}.{ext}`), so a name can never reach the disk.
- **Attaching.** Only the person who uploaded a file can attach it, and only once (`422 file_in_use`); someone else's upload reads as `404`. The file's row is locked while it is attached, so two requests can't attach it twice.
- **Who can download.** The person who uploaded it; anyone signed in for a profile photo or the logo; the chat's people for a message attachment; whoever may read the work entry for its attachment (its author with `work_entry:view_own`, or `work_entry:view_all`). Anyone else gets `404`, as if it didn't exist. Deactivated people's tokens are refused.
- **Cleanup.** Every 6 hours, uploads that have been attached to nothing for a day are deleted, row first, then the file on disk (`Jobs/FileCleanupJob.cs`). A photo or logo that is replaced or removed is attached to nothing any more, so it goes too.

## Storage

- Files are kept on the API's disk, under `FileStorage:RootPath` (`App_Data/files` by default, relative to the content root; gitignored). It must never be a folder the web server serves directly. Back it up together with the database.
- `FileStorage:CleanupEnabled` switches the cleanup job off; the integration tests do, and use a temporary folder per test class.
- One API instance for now. Several would need shared storage (a network folder or object storage) in place of `Services/Files/Implementation/LocalFileStorage.cs`.
