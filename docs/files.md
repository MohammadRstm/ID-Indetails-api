# Files

Photos, videos and voice notes: attachments on messages, work entries and daily logs, profile photos, projects' photos, the company logo and the company website's pictures and favicon ([website.md](website.md)). A file is uploaded first, and its id is then attached to what it belongs to.

Client portal files are separate: they go straight from the browser to the content store and are never served here. See [client-portal.md](client-portal.md#content-phase-2).

## Endpoints

| Endpoint | Who | What |
|---|---|---|
| `POST /api/files` | Signed in | Multipart field `file`, read as it arrives (not buffered first). `201` with `{ id, name, mimeType, kind, sizeBytes, createdAt }`: for a photo, those of the small WebP stored ([Photos](#photos)). Its own rate limit (`RateLimiting:UploadPermitsPerMinute`, 30 a minute), and at most 2 GB a person in any 24 hours (`422 upload_quota_reached`), so nobody can fill the disk |
| `POST /api/files/voice` | Signed in | A voice note: multipart field `file`, read as it arrives like a video, same rate limit and daily quota as `POST /api/files`. `201` with the same `FileResponse`, `kind: "audio"` ([Voice notes](#voice-notes)) |
| `GET /api/files/{id}?size=` | Signed in, see below | The content, shown inline, with `X-Content-Type-Options: nosniff`. Videos and voice notes can be read in ranges. `size` asks for a photo's smaller copy ([Photos](#photos)). A profile photo, the logo or a website picture is sent with `Cache-Control: private, max-age=86400`, so the browser keeps it for a day: its content never changes under its id and everyone signed in may see it. Anything else is `no-store`, because the browser's copy doesn't know who is signed in |
| `PUT /api/me/photo`, `DELETE /api/me/photo` | Signed in | `{ fileId }`: the caller's profile photo, from an upload of their own. Returns the signed-in user |
| `PUT /api/employees/{id}/photo`, `DELETE …` | `employee:manage` | `{ fileId }`: someone's profile photo, from the caller's upload. Other domains read as `404`; a super admin's needs `employee:invite_super_admin` |
| `PUT /api/projects/{id}/photo`, `DELETE …` | `project:edit` | `{ fileId }`: the project's own photo, shown instead of its icon tile, from the caller's upload (a photo). Other domains read as `404`. Returns the project |
| `PUT /api/company/logo`, `DELETE …` | `company_settings:manage` | `{ fileId }`: the company logo, at any time, before the rest of the settings too |
| `GET /api/company/branding` | Anyone | `{ name, hasLogo, logoFileId, supportWhatsApp }` for the sign-in page; `name` is null until the settings are saved |
| `GET /api/company/logo` | Anyone | The logo's image, or `404` |

Attachments go with what they are attached to: `attachmentIds` (up to 10, at most one of them a voice note) when sending a message ([messaging.md](messaging.md)), `attachmentFileId` when logging or editing a work entry ([design.md](design.md)), and `voiceFileId` when writing or editing a daily log ([execution.md](execution.md#site-staff-and-daily-logs)). Photo ids come back as `photoFileId` on the signed-in user, employees, chat contacts and the Employees page, on a project and the projects list, and as `logoFileId` on the company settings.

## Rules

`Business/Files/FileRules.cs`, `Services/Files/`:

- **Types.** Only photos (JPEG, PNG, WebP, HEIC, up to 10 MB) and videos (MP4, MOV, WebM, up to 100 MB) through `POST /api/files`, and voice notes (WebM, Ogg, MP4/M4A audio, up to 10 MB) through `POST /api/files/voice`. What a file is comes from its first bytes, never from its name or the type the browser sends (`422 file_type_not_allowed`, `422 file_too_large`). A profile photo or logo must be a photo.
- **Names** are only for display: path parts and unusual characters are dropped, up to 100 characters. The file is stored under a generated key (`yyyy/MM/{guid}.{ext}`), so a name can never reach the disk.
- **Attaching.** Only the person who uploaded a file can attach it, and only once (`422 file_in_use`); someone else's upload reads as `404`. The file's row is locked while it is attached, so two requests can't attach it twice.
- **Who can download.** The person who uploaded it; anyone signed in for a profile photo, the logo or a website picture (the public website serves its own through `/api/public/website/files/{id}`); whoever sees a project's business for its photo, as they see the project; the chat's people for a message attachment; whoever may read the work entry for its attachment (its author with `work_entry:view_own`, or `work_entry:view_all`); the log's writer and holders of `site_log:view_all` who can see the site's domain (Execution) for a daily log's voice note. Anyone else gets `404`, as if it didn't exist. Deactivated people's tokens are refused.
- **Cleanup.** Every 6 hours, uploads that have been attached to nothing for a day are deleted, row first, then the file on disk (`Jobs/FileCleanupJob.cs`). A photo or logo that is replaced or removed is attached to nothing any more, so it goes too. A file that is a daily log's voice note (`site_logs.voice_file_id`) counts as attached, so the cleanup never deletes it while the log points to it; a voice note removed from a log or replaced goes with the next cleanup.

## Photos

Photos are stored small (`Services/Files/Implementation/Pictures.cs`, SkiaSharp):
- **On upload**, a photo is:
  - turned upright, as the camera held it;
  - shrunk to at most **1920 px** on its longest side (the website shows a page picture at most about 1200 px wide);
  - saved as **WebP at quality 75**.

  A phone photo of 3–8 MB becomes roughly 100–300 KB. The browser shrinks it before sending too (the client's `preparePicture`), so uploads are quick; the server does it again for anything that didn't.
- **What's kept as it came:**
  - a photo already that small whose WebP wouldn't be smaller;
  - a photo SkiaSharp can't read (HEIC on Linux).
- **Too many pixels:** a photo whose header says it is over **50 megapixels** (`Pictures.MaxPixels`) is refused
  (`422 file_too_large`) before anything is decoded. Reading a photo takes 4 bytes a pixel, so a few-KB file declaring
  a huge picture could otherwise take the server's memory; 50 MP is more than a phone camera takes by default. The
  watermark check refuses one too, and the API container is limited to 2 GB (`mem_limit` in
  `deploy/docker-compose.prod.yml`), so whatever goes wrong there restarts the API, never MySQL.
- **Never cropped:** the website shows a card at two shapes (3:2 and 4:5), the page picture at a shape that follows the window, and gallery photos whole. Cropping would cut what one of them shows.
- **Smaller copies:** `?size=` on `GET /api/files/{id}` and on the public `/api/public/website/files/{id}`.
  - The size is the longest side wanted, rounded up to 160, 480, 960 or 1920 px. A 160 px preview is a few KB.
  - A copy is made the first time it is asked for, at WebP quality 70, and kept beside the photo as `{key}.{size}.webp`.
  - It is deleted with the photo. A photo already that small comes as it is, except to the public website.
- **The public website only ever gets a copy** (`IPictureSizes.OpenCopyAsync`): always re-encoded, so nothing of the
  uploaded file (such as the place a photo was taken) goes with it, even when the photo is already small. A photo no
  copy can be made of is `404` there, never its original. So a picture is taken for the website (a project's
  pictures, its gallery, the favicon) only if the server can read it: an iPhone HEIC photo is refused
  (`422 file_type_not_allowed`), with a note to save it as JPEG or PNG.
- **Videos** are stored as they come, written to disk as they arrive. One over 100 MB is removed and refused.

## Voice notes

- **A separate route** because WebM and MP4 look the same for audio and video in their first bytes, so the caller says it is a voice note. `POST /api/files` is unchanged: it takes no audio (an Ogg is refused, a WebM is still a video).
- **What is accepted**, from the file's first bytes (`FileRules.DetectVoice`):
  - **WebM** (the EBML header), stored as `audio/webm`, `.webm`;
  - **Ogg** (`OggS`), stored as `audio/ogg`, `.ogg`;
  - **MP4/M4A** (an ISO `ftyp` box with an MP4 brand or `M4A `), stored as `audio/mp4`, `.m4a`.

  Anything else is `422 file_type_not_allowed` ("Voice notes can be WebM, Ogg or MP4 audio."). The type and name stored come from the bytes, never from the browser.
- **At most 10 MB**: written to disk as it arrives, and removed and refused when bigger (`422 file_too_large`, "Voice notes can be at most 10 MB."), as a video is.
- **`kind` is `audio`** in `FileResponse` (`FileRules.KindOf` maps any `audio/*` type to it).
- **Playing** is the usual `GET /api/files/{id}` (range requests work, `no-store`). Who may: the sender's chat for a message, the writer and the readers of everyone's logs for a daily log ([Rules](#rules)).
- **Where it attaches:** one per chat message ([messaging.md](messaging.md)) and one per daily log ([execution.md](execution.md#site-staff-and-daily-logs)). A log's `voiceFileId` must be an audio file (`422 file_type_not_allowed`, "A voice note must be a recording.").

## The invisible watermark

The website's pictures carry a mark in their pixels that says they are the company's, and which picture they are (`Services/Files/Implementation/Watermark.cs`).
- **What carries it:** every copy of **480 px and up** (`PictureSizes`). That includes all the website gets, since `/api/public/website/files/{id}` never serves an original: without `size`, it serves the 1920 copy. The stored originals and the 160 px previews don't carry it.
- **How:**
  - The picture is seen as a 256×256 grid of 16×16 blocks, whatever its size.
  - Each block carries one bit of a 32-bit code (a company tag, then the picture's id), in the balance of two mid-frequency values. Each bit is spread over 8 blocks, placed and signed by a secret key.
  - The change is capped so that no pixel moves by more than about 3 levels in 255, smoothly: it can't be seen.
- **What it survives:** resizing, re-saving as JPEG or WebP, and screenshots, because it is read on the same grid at any size. Heavy cropping, filters, or a tool built to remove it can defeat it.
- **Checking a picture:** `POST /api/website/watermark-check` (`website:manage`) takes the multipart field `file` (JPEG, PNG or WebP, up to 15 MB). It answers `{ found, fileId, projectTitle, confidence }`.
  - `confidence` is the share of blocks that agree: about 0.5 by chance, 0.9 or more for a marked picture.
  - A reading counts as found from 0.7, with the company tag. The dashboard has it on Website › Site.
- **The key:** `FileStorage:WatermarkKey`, from `WATERMARK_KEY` in the server's `.env`. Set it once: pictures marked with an older key can no longer be read.
- **Cost:** about 0.1–0.3 s of server time per copy, once, since copies are kept.

## Storage

- Files are kept on the API's disk, under `FileStorage:RootPath` (`App_Data/files` by default, relative to the content root; gitignored). It must never be a folder the web server serves directly. Back it up together with the database.
- `FileStorage:CleanupEnabled` switches the cleanup job off; the integration tests do, and use a temporary folder per test class.
- One API instance for now. Several would need shared storage (a network folder or object storage) in place of `Services/Files/Implementation/LocalFileStorage.cs`.
