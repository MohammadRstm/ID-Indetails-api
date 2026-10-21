# Client portal

A Design project's client can follow the project in a client portal.
- **Phase 1:** client accounts, invitations, the clients' own sign-in, and access per project.
- **Phase 2:** staff prepare the portal's files (presentation, 3D, drawings) as drafts in a private content store. See
  [Content (Phase 2)](#content-phase-2).
- **Phase 3:** the draft is arranged (order, hero image, introduction), previewed, and published project-wide; clients
  see the published presentation. See [Publishing and the client's page (Phase 3)](#publishing-and-the-clients-page-phase-3).
- **Phase 4:** the project's discussion: a chat, comments on published files, @mentions, and in-app notifications on
  both sides, live. See [Discussion (Phase 4)](#discussion-phase-4).
- **Project files:** the files of Phases 2 and 3 became every Design project's files, for its whole team, with
  comments. The client portal shows only the files chosen for the client (`for_client`). See
  [Project files](#project-files); it changes some of what Phases 2 and 3 say, as noted there.
- **Portal UX:** up to 12 hero images instead of one, deleting a chat message for everyone, and the client's project
  list with a cover, dates and a "news" flag. See [Portal UX](#portal-ux).

**Execution projects have no client portal and no project files.** Their endpoints answer 404, and the permissions are
limited to Design.

## Identity: separate from staff

Clients are not staff users. They never appear in staff lists, contacts, notifications, search, presence or Proton,
because:
- **Separate tables:** clients live in `portal_accounts`. The `users` table is untouched.
- **One account per email**, unique among portal accounts. One account can have access to several projects
  (`portal_project_access`).
- **Their own token scheme** (`Portal`), with the same issuer and key but its own audience (`Portal:Audience`, default
  `id-indetails-portal`):
  - Staff endpoints validate the staff audience, so they refuse a client's token.
  - Portal endpoints (`[Authorize(Policy = "Portal")]`) authenticate only the portal scheme, so they refuse a staff token.
- **No staff user id in a client's token:**
  - A portal token carries the account id as `portal_account` and **no `sub`**.
  - Everything that reads a staff user id from `sub` therefore finds none: permissions, last seen, presence, the per-user
    rate limit and the security log.
  - The portal scheme also refuses any token that has a `sub`.
- **Own refresh cookie:**
  - `idi_portal_refresh`: HttpOnly, Secure, `SameSite=Strict`, `Path=/api/portal/auth`, 1 day (`Portal:RefreshTokenDays`).
  - The staff cookie stays on `/api/auth`. Neither is ever sent to the other's endpoints, so a staff member and a client
    can be signed in in the same browser.
- **Rotation and reuse detection:** refresh rotates the token, as for staff on the web. Presenting a rotated token more
  than 30 seconds later ends every session of the account.
- **Ending sessions:**
  - Each token is checked against `portal_accounts.sessions_valid_from` and the account's status on every request.
  - A password reset, or the end of the account's last project access, signs it out everywhere: it sets that cut-off and
    revokes the refresh tokens.

## Who can invite

- **Permission:** `client_access:manage`, limited to Design. It is a default for admins (effective only for Design admins)
  and for super admins, and can be granted like any other.
- **What it covers:**
  - listing a project's client contacts and where each stands;
  - inviting;
  - resending;
  - cancelling an invitation;
  - ending access.
- **It is checked twice:** by the endpoint attribute and again in `PortalAccessService`.
- **Who can be invited:**
  - only an **email contact of the project's client**, so invitations never go to arbitrary addresses;
  - on a Design client project that isn't archived, whose client isn't archived.
- **Checked again when the link is opened and used** (`PortalRules.CanAccept`): if the project or its client has been
  archived, or the address is no longer one of the client's contacts, the link reads as cancelled. Moving the project to
  another client cancels its invitations outright.

## Invitation flow

1. **Invite.** Staff call `POST /api/projects/{id}/portal/invitations { email }`.
   - **New address:** an `invited` account is created, and an invitation with a 256-bit token valid **24 hours**
     (`Portal:InvitationExpirationHours`) is emailed.
   - **No password is ever emailed.** Only the token's SHA-256 is stored.
   - The email is sent inside the transaction: if it can't be delivered, nothing is saved (503).
2. **The client opens the link** (`/portal/invite?token=…`). `POST /api/portal/invitations/validate` shows the email and the
   project, or why the link can't be used:
   - `404 portal_invitation_invalid`: no such link;
   - `422 portal_invitation_expired`;
   - `422 portal_invitation_revoked`: cancelled, replaced by a resend, or no longer allowed (see "Checked again when the
     link is opened and used" above);
   - `422 portal_invitation_used`: the account is already set up, so sign in.
3. **The client sets up the account.** `POST /api/portal/invitations/accept { token, firstName, lastName, password }`:
   - The password follows the staff rules (10–128 characters, PBKDF2 hashing).
   - The token is claimed atomically, so it can be used **once**.
   - The account becomes `active` and gets access to the project, plus any other project it had a pending invitation to
     that still passes the same check. Each project is opened once; every other pending invitation of the account (one
     that fails the check, or a second one to the same project) is cancelled, so none of its links stays usable.
   - The client is signed in.
4. **An address that already has an active account** gets access to the new project at once, and an email saying so. No
   second account and no new password.
5. **Resend** (`…/invitations/{id}/resend`) replaces the link; the old one reads as revoked. **Cancel**
   (`…/invitations/{id}/revoke`) stops a pending link. Cancelling is a single conditional update, like accepting: an
   invitation the client has just accepted can't be cancelled (`422 portal_invitation_used`), and their access stays.
6. **End access** (`…/portal/access/{id}/revoke`): the project disappears from the client's portal at once, and their
   pending invitations to it are cancelled. If it was their last project, they are signed out everywhere.
7. **Moving the project to another client, or making it internal**, ends every client's access to it.
8. **Archiving the client** ends every contact's access to all of the client's projects and cancels their pending
   invitations; anyone left with no project is signed out. **Restoring the client gives nothing back**: staff invite again.
9. **Taking an email address off the client** (removing the contact, or changing its address) ends that address's
   access to all of the client's projects and cancels its pending invitations, in the same save; if it had no other
   project, it is signed out everywhere. The client's other contacts keep theirs.

**At the same moment.** Inviting, resending and accepting for one address lock its account row first, so they take
turns: two invitations sent at once never leave two pending links to one project, and two links of one account used at
once can't claim each other's invitations.

## Client sign-in

All of these are under `/api/portal/auth`:
- `login`
- `refresh` (cookie)
- `logout`
- `forgot-password` (always 204; the link goes to `/portal/reset-password` and lasts 30 minutes)
- `reset-password` (signs out everywhere)

Errors:
- Unknown emails and accounts not set up yet get the same answer and the same timing as a wrong password, through a dummy
  hash.
- After **10 wrong passwords** in a row, sign-in pauses for **15 minutes** (`Portal:LockoutAttempts`,
  `Portal:LockoutMinutes`). During the pause even the right password gets the same generic answer, so the pause can't be
  used to test passwords. Each wrong password is counted in the database in a single statement, so wrong passwords sent
  at once all count.
- Rate limits: the per-IP `auth` limit applies to login, invitations and resets; `refresh` to refresh; staff invitation
  emails use the per-person `email` limit.

`GET /api/portal/me` (portal token only) returns the account and the Design projects it may open, newest first, with
what each project's card shows (see [The client's project list](#the-clients-project-list)).

## Endpoints

| Method and path | Who | |
|---|---|---|
| `GET /api/projects/{id}/portal/clients` | `client_access:manage` | The client's email contacts and their status: `not_invited`, `invited`, `invitation_expired`, `active`, `access_ended` |
| `POST /api/projects/{id}/portal/invitations` | `client_access:manage`, `email` limit | `{ email }` |
| `POST /api/projects/{id}/portal/invitations/{invitationId}/resend` | same | |
| `POST /api/projects/{id}/portal/invitations/{invitationId}/revoke` | `client_access:manage` | |
| `POST /api/projects/{id}/portal/access/{accessId}/revoke` | `client_access:manage` | |
| `POST /api/portal/invitations/validate`, `/accept` | anyone with the link, `auth` limit | |
| `POST /api/portal/auth/login`, `/refresh`, `/logout`, `/forgot-password`, `/reset-password` | the client | |
| `GET /api/portal/me` | portal token | |

Proton never calls any of these: they are `Never` in `ProtonCoverage`.

## Audit

- **Activity feed.** Invitations, resends, cancellations, acceptance and access granted or ended are written to
  `activity_events`, with the project, the client and `subject_type = portal_account`. They appear under Projects in the
  Design activity feed.
  - An acceptance has no staff actor.
  - These events count as project activity for auto-archive. So do the project's chat messages and (not deleted)
    comments: a completed project whose discussion went on in the last 90 days isn't archived automatically.
- **Security log.** Sign-in refusals, pauses and reused refresh tokens go to the security log
  (`ID_Indetails.api.Security`).

## Tables (migration `ClientPortalAccounts`)

All foreign keys restrict deletion; nothing cascades.

### `portal_accounts`
- Columns: `email` (unique), `first_name`, `last_name`, `password_hash`, `status` (`invited` / `active`), `sessions_valid_from`, `failed_sign_ins`, `locked_until`, `activated_at`, `last_seen_at`, `created_at`, `created_by_id`, `updated_at`.
- Foreign key: `created_by_id` → `users`.
- CHECK: an active account has a password and a name.

### `portal_project_access`
- Columns: `account_id`, `project_id`, `granted_at`, `granted_by_id`, `revoked_at`, `revoked_by_id`.
- Foreign keys: `account_id` → `portal_accounts`, `project_id` → `projects`, `granted_by_id` and `revoked_by_id` → `users`.
- Unique on `(account_id, project_id)`: ending access sets `revoked_at`, and giving it again reuses the row. The history is in the activity feed.

### `portal_invitations`
- Columns: `account_id`, `project_id`, `token_hash` (unique), `sent_at`, `expires_at`, `accepted_at`, `revoked_at`, `invited_by_id`.
- Foreign keys: `account_id` → `portal_accounts`, `project_id` → `projects`, `invited_by_id` → `users`.
- Indexes on `(account_id, project_id)` and on `project_id`.

### `portal_refresh_tokens`
- Columns: `account_id`, `token_hash` (unique), `session_id`, `created_at`, `expires_at`, `revoked_at`, `replaced_by_id` (self), `revoke_reason`.
- Foreign keys: `account_id` → `portal_accounts`, `replaced_by_id` → `portal_refresh_tokens`.

### `portal_password_reset_tokens`
- Columns: `account_id`, `token_hash` (unique), `created_at`, `expires_at`, `used_at`.
- Foreign key: `account_id` → `portal_accounts`.

### Seed
- The permission `client_access:manage` (domain `design`), in the admin and super admin role defaults.

### Rollback
- `Down` drops the five tables and removes the seed rows. Any portal accounts are lost.
- The migration only adds, so an older API runs on the newer schema.

## Configuration (`Portal` section)

| Key | Default |
|---|---|
| `Audience` | `id-indetails-portal` |
| `InvitationExpirationHours` | 24 |
| `PasswordResetExpirationMinutes` | 30 |
| `RefreshTokenDays` | 1 |
| `CookieName` | `idi_portal_refresh` |
| `CookiePath` | `/api/portal/auth` |
| `LockoutAttempts` | 10 |
| `LockoutMinutes` | 15 |

The email links use `Frontend:PortalInvitationPath` (`/portal/invite`), `PortalLoginPath` (`/portal/login`) and
`PortalResetPasswordPath` (`/portal/reset-password`).

## Content (Phase 2)

> Since [Project files](#project-files), these files are every Design project's files, open to its whole team, and the
> endpoints below live under `/api/projects/{id}/files`. Who may do what is in that section; how files reach the store,
> what is accepted and how storage is counted are unchanged.

Staff prepare the files a client will see: **Presentation**, **3D** and **Drawings**. Everything is a **draft** until
publishing exists: no client endpoint reads content, the store is private, and links are only handed out to staff after
the permission check.

### Where the bytes go
- Files live in the **content store**: Cloudflare R2 in production, a folder on disk in development and tests
  (`ContentStore:Provider`; see [content-storage.md](content-storage.md)). Never on the server's disk in production,
  and never through `/api/files`.
- The browser sends each file **in parts, straight to the store**, through short-lived signed links. The API never
  carries the bytes, so the server's memory, disk and request limits don't matter. Each file can be at most **1 GB**
  (`PortalContentRules.MaxFileBytes`); a bigger one is refused with `422 validation_failed` when the upload starts.
- Parts are all one size but the last: 16 MiB.
- Keys are `portal/{projectId}/{random}`: unguessable, never the file name. The name is kept in the row and sent back
  as the download name.

### Upload lifecycle
1. `POST .../uploads` writes a `portal_uploads` row with the key **before** anything reaches the store, then starts a
   multipart upload. The same client token again returns the same upload, once the store has started it; one the
   store hasn't started yet (two starts at the same moment), or whose start failed, answers 409 `portal_upload_ended`,
   and the browser starts again with a new token.
2. `POST .../uploads/{id}/parts` hands out links (about an hour each, up to 100 at a time). The browser PUTs each part.
3. `POST .../uploads/{id}/complete`:
   - the server lists the parts itself (nothing the browser says about them is trusted);
   - it joins them, checks the size against the one announced, and checks the first bytes against the extension;
   - a file that isn't what its extension says is deleted and the upload marked `rejected`;
   - in one transaction, with the upload locked, it adds the file and the item (or swaps the file into the item it
     replaces). Repeating the call, even at the same moment, returns the same item: completions of one upload run one
     at a time, so a repeat waits for the join in progress and then finds the upload completed. If the store finished but the
     database didn't, the retry carries on from the joined object; when the store no longer has the upload (joined
     already, or abandoned), the object decides: there, it carries on; gone, 409 `portal_upload_ended`.
4. `DELETE .../uploads/{id}` cancels: the row is marked first, then the parts and any object are dropped.

**Consistency.** The rows lead:
- every object's key is in a row before the object exists;
- a file no longer used is marked (`deleted_at`) in the same transaction that stops using it, and its object is
  deleted afterwards, then the row;
- whatever fails there, the hourly cleanup (`PortalContentCleanupJob`) finishes:
  - uploads with no request for 24 hours are expired, and their parts and objects removed;
  - files marked deleted lose their objects, then their rows;
  - ended uploads are forgotten after a week.
- Once a week the job lists `portal/` in the store and logs unknown objects, missing objects and size differences. It
  deletes unknown objects only when `PortalContent:DeleteOrphans` is on: after a database restore, newer files would
  look unknown.

### What is accepted
Images (JPG, PNG, WebP, GIF, HEIC, TIFF), video (MP4, M4V, MOV, WebM), PDF, Office (DOCX, XLSX, PPTX), ZIP, drawings
(DWG, DXF, RVT, IFC, SKP, 3DM) and 3D models (GLB, glTF, OBJ, FBX, STL, 3DS, MAX, BLEND, USDZ, DAE).
- The type comes from the extension and is checked against the first 4 KB once the file has arrived. Programs and
  web pages are refused whatever their extension; SVG and HTML are never taken.
- The content type is the server's, never the browser's.
- **Previews:** JPEG, PNG, WebP and GIF images and MP4/WebM videos inline; PDFs open in the browser's own viewer in a
  new tab; everything else is downloaded. Nothing is converted, recompressed or altered.

### Sizes and storage use
Each file keeps its **original size** (the file the person picked) and its **stored size** (what the store reports for
the object, measured when the upload completes). They are equal: nothing is compressed. A project's usage (files,
original and stored bytes) is a sum over its live rows, so it never lists the store on a page load; the weekly check
keeps the rows honest. Usage is informational: nothing is refused because of it.

### Order and drafts
- Items keep their place in a section in `sort_order`, with gaps of 1,024 so a later phase can move one between two
  others; ties go by id. New files go to the end; moving to another section puts the item at its end.
- Replacing a file keeps the item, its title and its place.
- Publishing (a later phase) adds published columns to `portal_items`. Every item today then reads as never published.

### Who can do what
See [Project files](#project-files) for who may change which file. Throughout:
- Execution projects and other domains' projects answer 404.
- Only the person who started an upload can carry it on, complete or cancel it.
- An archived project is read-only (422 `portal_content_read_only`); its files can still be opened and downloaded.
- Client tokens are refused by every content endpoint (401). The content store's links are the only way to the bytes,
  minted after these checks and short-lived.

### Content endpoints (now under `/api/projects/{id}/files`)

| Method and path | Purpose |
|---|---|
| `GET /api/projects/{id}/files` | Every section's items, usage, whether it can change, accepted extensions |
| `POST /api/projects/{id}/files/uploads` | Start an upload `{ clientToken, section, fileName, size, replaceItemId? }` |
| `POST .../uploads/{uploadId}/parts` | Links for `{ partNumbers }` |
| `POST .../uploads/{uploadId}/complete` | Join, check and add |
| `DELETE .../uploads/{uploadId}` | Cancel |
| `PUT .../items/{itemId}` | Rename `{ title }` or move `{ section }` |
| `DELETE .../items/{itemId}` | Delete the item and its file |
| `GET .../items/{itemId}/link?inline=` | A five-minute link to open or download |

All the writes are `Never` for Proton.

### Tables (migration `ClientPortalContent`)
- `portal_uploads`: uploads on their way (project, section, the item replaced, client token, key, store upload id,
  file name, announced size, status `uploading` / `completed` / `cancelled` / `rejected` / `expired`, who, when, last
  activity). Unique key; unique `(created_by_id, client_token)`; `(status, last_activity_at)` for the cleanup.
- `portal_files`: objects in the store (project, key, original name, content type, original and stored size, uploader,
  when, `deleted_at`). Unique key; `(project_id, deleted_at)` for usage.
- `portal_items`: what a section shows (project, section, file, title, sort order, created, updated, updated by).
  Unique file; `(project_id, section, sort_order)`.
- Seed: the permission `client_portal:manage` (Design) for admins and super admins (admins lost it in `ProjectFiles`).
- All foreign keys restrict.
- **Rollback** drops the three tables and the seed, but not the objects in the store: empty `portal/` in the bucket
  first if the content should go too.

### Configuration (`PortalContent` section)
| Key | Default | Meaning |
|---|---|---|
| `UploadInactivityHours` | 24 | An upload with no request for this long is abandoned |
| `PartLinkMinutes` | 60 | How long a part link lasts |
| `PreviewLinkMinutes` | 60 | How long the list's image and video links last |
| `DownloadLinkMinutes` | 5 | How long an open or download link lasts |
| `JobsEnabled` | true | The hourly cleanup and weekly check (off in integration tests) |
| `DeleteOrphans` | false | Whether the weekly check deletes objects no row knows about |
| `OrphanGraceDays` | 7 | How old an unknown object must be before it counts |

## Publishing and the client's page (Phase 3)

Staff with `client_portal:manage` work on a **draft**; clients see the **published copy**, which only **Publish changes**
replaces, all of it at once. Since [Project files](#project-files), the draft's files are the project files chosen for
the client.

### Draft and published copies
- `portal_items` keeps both: the plain columns are the draft, `published_file_id`, `published_title`,
  `published_section` and `published_sort_order` what clients see (all set or all null; null = never published).
  `for_client` says whether the file is chosen for the client.
- `portal_settings` (one row per project) keeps the draft and published introduction (`summary`, `published_summary`),
  the `revision`, and the last publication (`published_revision`, `published_at`, `published_by_id`). The hero images
  are rows of `portal_hero_images`: the draft list (`is_published = 0`) and the published one (`= 1`), each in
  `sort_order` (see [Portal UX](#portal-ux)).
- Every change to the files, the choices, the hero images or the introduction takes the project's content lock (the
  settings row, `SELECT ... FOR UPDATE`). The row is made, or locked exclusively at once if it exists, by
  `INSERT ... ON DUPLICATE KEY UPDATE`, so changes at the same moment wait for each other rather than deadlock.
- `revision` moves on only with what changes the next publish: a change to a **client-relevant** file (chosen for the
  client, or still published), a choice, the hero images or the introduction. Renaming or adding a file the team keeps to
  itself doesn't make a publish stale.
- A client-relevant item's state: **new** (chosen, never published), **changed** (file, title or section differ),
  **unchanged**, or **removed** (published, then deleted or no longer chosen; clients keep it until the next publish).
  Deleting a published item only sets `removed_at`, and **restore** undoes it. A never-published item is deleted at
  once, with its comments.
- Replacing a published file keeps the old one for clients until the next publish.

### Publishing
`POST .../content/publish { expectedRevision }`, in one transaction under the content lock:
1. refused (409 `portal_revision_stale`) when the drafts moved on since the revision the person saw, and (422) on an
   archived or non-client project;
2. removed items are deleted; each chosen item's draft is copied into its published columns; a published item no
   longer chosen has its published columns cleared (it stays one of the project's files); the published hero images
   become the draft's (same images, same order) and the introduction is copied, and the publication recorded;
3. files nothing points at any more are let go (below), and one `portal_published` activity event is written: the hook
   for client notifications later (none are sent now).

Clients see the old version or the new one, never a mix: a failed publish rolls back and changes nothing. With nothing
to publish it just answers the content.

**Discard** (`POST .../discard { expectedRevision }`) puts back only the client's side: every item not removed is
chosen again exactly when clients see it now (`for_client = published_file_id IS NOT NULL`), the draft hero images
become the published ones (same images, same order), and the introduction goes back to the published one. Titles,
sections, order, replaced files and deletions belong to Project files and stay as they are. It needs a publication to
go back to (422 `portal_never_published`).

### Files are let go only when nothing uses them
A file is marked deleted only when no item's draft or published copy and no draft or published hero image points at
it, checked inside the change that drops the reference (`PortalReferences`). The cleanup checks again before deleting
the object, and the restricting foreign keys refuse to delete a row still used. So a hero image, kept by its file rather
than by an item, survives its item being deleted or replaced; a file uploaded as a hero image and then taken off (or
discarded) before it was ever published goes.

### Order and hero images
- `PUT /api/projects/{id}/files/sections/{section}/order { itemIds }` (`project_files:manage`) takes the section's
  current (not removed) items, each once; anything else is 409 `portal_order_stale` (someone added, removed or moved a
  file). Drag and drop is within a section; moving between sections stays the Phase 2 "Move to".
- The hero images: see [Portal UX](#hero-images).
- `PUT .../summary { summary }`: plain text, up to 2,000 characters.

### Preview and the client's page
- `GET .../content/preview` (staff) and `GET /api/portal/projects/{id}` (client) answer the same shape, built by the same
  code (`PortalContentViews.PresentationAsync`): the draft (the chosen items not removed) for the preview, the published
  copy for the client. It holds the project's title and status, the introduction, the hero images (`heroes`, in order,
  each `{ url, urlExpiresAt, name }`; `hero` is the first, or null, for the phone app) and the three sections, and
  nothing about staff, uploaders or storage.
- Opening `GET /api/portal/projects/{id}` records when the client opened the project
  (`portal_project_access.last_opened_at`, one `UPDATE`), for the project list's news flag.
- `GET /api/portal/projects/{id}/items/{itemId}/link?inline=` gives a five-minute link to a **published** file.
- A client gets anything only with live access to the Design project; every refusal (no access, access ended, another
  project, an unpublished file) is the same 404. Before the first publish the page is empty (`isPublished: false`).
- Completed projects stay published to their clients, and staff can still change and publish them. Archived projects
  stay visible to clients but can't change or be published.
- Signed links carry `Cache-Control: private, max-age=` the whole seconds the link has left (at least 0; for R2, counted
  when the link is made), so the browser doesn't keep what it loaded much past the link's end: an hour for a preview,
  five minutes for a download. There are no thumbnails: the original is loaded, lazily.

### Endpoints added (staff)
The order and restore moved to [Project files](#project-files) (`.../files/sections/{section}/order`,
`.../files/items/{itemId}/restore`).

| Method and path | Who | Purpose |
|---|---|---|
| `POST .../content/heroes`, `DELETE .../content/heroes/{fileId}`, `PUT .../content/heroes/order` | `client_portal:manage` | The draft hero images (since Portal UX; `PUT` / `DELETE .../content/hero` are gone) |
| `PUT .../content/summary` | `client_portal:manage` | The draft introduction |
| `POST .../content/publish` | `client_portal:manage` | Publish everything |
| `POST .../content/discard` | `client_portal:manage` | Back to what clients see |
| `GET .../content/preview` | `client_portal:manage` or `client_access:manage` | The draft in the client's shape |

All writes are `Never` for Proton. Client endpoints: `GET /api/portal/projects/{id}` and `GET .../items/{itemId}/link`.

### Tables (migration `ClientPortalPublishing`)
Adds the published columns and `removed_at` to `portal_items` (with a check that the published copy is whole, and an
index for the client's page), the table `portal_settings`, and `is_hero` to `portal_uploads` (whose `section` becomes
optional: a hero upload has none). Only adds; the rollback drops them.

## Discussion (Phase 4)

Each Design client project has a **discussion**: one chat for the whole project, and a comment thread on each
published file. The same API serves both sides (`PortalDiscussionService`, with the caller's side).

### Who takes part
Worked out on every request, never stored and never taken from the browser:
- **staff:** every active super admin; and the project's manager (its owner), active staff who uploaded the current
  file of an item chosen for the client or published, and those who logged work on the project, as long as they can
  see Design (their home domain, or a Design permission they were granted, as for every other Design record). A file
  the team keeps to itself doesn't bring its uploader in. Nobody else, whatever their permissions;
- **clients:** active accounts with live access to the project.

Anyone else gets a 404, the same as for a project that doesn't exist. Ending a client's access, deactivating a staff
member, or moving them to Execution takes them out at once.

### Chat and comments
- Plain text only (rendered escaped by the client), up to 4,000 characters in the chat and 2,000 in a comment.
- Each message or comment carries a **client token** made by the browser: sending it again returns the first one and
  notifies nobody twice (unique per author and token).
- **Authors can edit** their messages and comments (shown "edited"; the earlier wording isn't kept) and **delete**
  them for everyone: a deleted message or comment keeps its place without its text and mentions (`body` null,
  `deleted: true`), can't be edited, and is never unread. Deleting again changes nothing. Only the author may (403
  otherwise), and not on an archived project (422 `portal_discussion_closed`); editing a deleted one is 422
  `portal_discussion_closed` too.
- **Comments are on published files only.** Each keeps the item and file it was written about and their names. When the
  file is replaced and republished, older comments read "on an earlier version"; when the file is unpublished, its
  thread stays readable, closed, and is never attached to another file. Drafts can't be commented on or found.
- **Threads are flat**: a file's comments in order.
- **Mentions**: the people taking part, by key (`u{id}` staff, `c{id}` clients), checked on the server. A mentioned
  person gets a mention notification once; an edit that adds someone notifies them.
- **Read markers** only move forward, and never past what exists: one per person on the project's chat
  (`portal_reads`), and one per person on each file's comments (`portal_comment_reads`), so reading one file's comments
  leaves the others new. `POST {base}/read` takes `lastMessageId`, and/or `itemId` with `lastCommentId`. Two tabs
  writing a person's first marker at once keep one: the second finds the first and moves it.
- **Archived** projects: everything readable, nothing writable (422 `portal_discussion_closed`). **Completed** projects
  work as usual.

### Notifications
- **Staff** use the usual notifications (and pushes, when they're on): `portal_client_message` (rolling per project),
  `portal_client_comment` (to the manager and the file's uploader, rolling per file) and `portal_mention`. A rolling one
  counts what came since the person last read it (it starts again at 1 once read). See
  [notifications.md](notifications.md).
- **Clients** have their own (`portal_notifications`): `team_message` (rolling per project), `team_comment` (rolling per
  file) and `mention` (once). A rolling one adds to its count while unread and starts again at 1 once read. Each is
  written in one statement (`INSERT ... ON DUPLICATE KEY UPDATE`), so messages sent at the same moment still make one
  notification counting them all. They're shown and counted only while the client has access to the project. `GET /api/portal/me` gives each project's unread count.
- No notification holds the text written: only who, which project and which file.

### Live
- Staff: the existing hub (`/hubs/live`) gets a `portal` signal (`{ projectId }`).
- Clients: their own hub, **`/hubs/portal`** (`PortalHub`): portal tokens only, each connection in its account's group,
  signals `portal` (`{ projectId }`) and `notifications`. No signal carries content; pages fetch what changed.

### Rate limit
Clients' writes (send, edit, comment, delete) are limited per portal account and address
(`RateLimiting:PortalWritePermitsPerMinute`, 30). The token is read unchecked to pick the bucket, so the address is part
of it: a made-up token naming someone's account can't use up theirs. Their other requests still count against the
per-address limit (below).

### Endpoints
Staff: `/api/projects/{id}/portal/discussion`; clients: `/api/portal/projects/{id}/discussion`.

| Method and path | Purpose |
|---|---|
| `GET {base}` | Summary: whether it can be written in, unread counts, participants (for mentions), comment threads |
| `GET {base}/messages?before=&limit=` | A page of the chat, oldest first (newest page without `before`) |
| `POST {base}/messages` / `PUT` / `DELETE {base}/messages/{id}` | Send / edit / delete a message (for everyone) |
| `GET {base}/comments/{itemId}` | A file's thread |
| `POST {base}/comments` / `PUT` / `DELETE {base}/comments/{id}` | Comment / edit / delete |
| `POST {base}/read` | Move the read markers |
| `GET /api/portal/notifications`, `GET .../unread-count`, `POST .../read` | The client's bell |

All writes are `Never` for Proton.

### Tables (migration `ClientPortalDiscussion`)
- `portal_messages`, `portal_comments`: author (a user or a portal account, exactly one), text, client token (unique per
  author), `edited_at`, `deleted_at` (a check that only a deleted one has no text; messages since migration
  `PortalUx`); comments also the item and file ids (plain columns, not foreign keys: items are unpublished and files
  replaced later), the item's title and file name.
- `portal_mentions`: a message or a comment, and a user or an account (exactly one each).
- `portal_reads`: one chat marker per person and project.
- `portal_comment_reads`: one comment marker per person and file (the item id, a plain column as on the comments).
- `portal_notifications`: the client's notifications, unique per account and key.

## Project files

Every Design project, for a client or internal, has **Project files**: the presentation, 3D and drawing files its team
works on, in the same three sections and the same content store as above. They are the project's own; a client sees a
file only once it is **chosen for the client** (`portal_items.for_client`) and published. The client portal tab shows
the chosen files, with the hero images, the introduction, the preview, publishing, client access and the discussion.

### Who may do what
Everything is on Design projects only; anything else answers 404. An archived project is read-only (422
`portal_content_read_only`); its files and comments can still be read, opened and downloaded.

| Action | Who |
|---|---|
| See the files, open and download them, read their comments | anyone with `project:view` who can see Design (employees included) |
| Add a file | the same, on any Design project |
| Rename, move, replace, delete or restore a file | whoever uploaded its current file (`portal_files.uploaded_by_id` of the item's file), or a holder of `project_files:manage` |
| Reorder a section | `project_files:manage` |
| Comment | `project_files:manage` on any file; anyone else only on a file that already has comments (a reply) |
| Edit or delete a comment (deleting is for good) | its author |
| Mark a comment fixed, or open again | the file's uploader or `project_files:manage` |
| Choose a file for the client, or take it back | `client_portal:manage`, on a client project (422 `portal_project_not_eligible` otherwise) |
| Look at the client portal tab: its files, preview and links | `client_portal:manage` or `client_access:manage` |
| Hero, introduction, publish, discard, hero uploads | `client_portal:manage`, on a client project |

- `project_files:manage` (Design) is a default for admins and super admins. `client_portal:manage` is the super admins'
  by default (admins see the client portal through `client_access:manage`), and can be granted like any other.
- Replacing a file makes whoever replaced it its uploader, and so the one who may change it from then on.
- Refusals are 403 `forbidden`. Someone else's upload, another project's file or comment, and a project they can't see
  read as 404.

### What the client portal shows
A file is **client-relevant** when it is chosen for the client or clients still see it (published). Only those concern
the client portal: its tab lists them, its link endpoint opens only them, their changes move the revision on, and
they decide the states and what a publish changes. A file the team keeps to itself never reaches the client, the
preview or the revision. See [Publishing](#publishing) for what publishing and discarding do with the choices.

Usage (files and bytes) counts all of the project's files.

### Comments
- Plain text, 1 to 2,000 characters once trimmed; flat, oldest first. Edited ones keep `edited_at`.
- A comment is **open** until marked fixed (`fixed_at`, `fixed_by_id`); each file shows how many comments it has and
  how many are open.
- A new comment notifies the file's uploader and everyone who commented on it before, never its author:
  `project_file_comment`, rolling per file (`file_comments:{itemId}`, counting from 1 again once read), naming who,
  which file and which project, never the text. See [notifications.md](notifications.md).
- Clients never see these comments: the client discussion has its own.

### Endpoints (`/api/projects/{id}/files`, `project:view`; finer rules in the services)
| Method and path | Purpose |
|---|---|
| `GET /` | Every file (removed ones too, marked `removed`, until the next publish), usage, accepted extensions, and what the caller may do: `canEdit`, `canManage`, `canChooseForClient`, `isClientProject` |
| `POST /uploads` | Start an upload `{ clientToken, section, fileName, size, replaceItemId? }`, or a hero `{ hero: true }` |
| `POST /uploads/{uploadId}/parts`, `POST .../complete`, `DELETE /uploads/{uploadId}` | As in [Phase 2](#upload-lifecycle); completing answers `{ item, usage }` |
| `PUT /items/{itemId}` | Rename `{ title }` or move `{ section }` |
| `DELETE /items/{itemId}` | Delete (a published one is removed until the next publish) |
| `POST /items/{itemId}/restore` | Undo a removal |
| `GET /items/{itemId}/link?inline=` | A five-minute link to open or download |
| `PUT /sections/{section}/order` | A section's new order `{ itemIds }` |
| `PUT /items/{itemId}/for-client` | Choose for the client or take back `{ forClient }` |
| `GET /items/{itemId}/comments`, `POST` the same | A file's comments, oldest first; comment `{ body }` |
| `PUT` / `DELETE /comments/{commentId}` | Edit `{ body }` / delete |
| `PUT /comments/{commentId}/fixed` | Mark fixed or open again `{ fixed }` |

Each file says its uploader, whether the caller may change it (`canEdit`), its comment counts (`comments: { total,
open }`), whether it is chosen (`forClient`) and its state for the client (`clientState`, null for a file that isn't
for the client and never reached them). All the writes are `Never` for Proton.

The client portal tab keeps `GET`, the hero images (`POST /heroes`, `DELETE /heroes/{fileId}`, `PUT /heroes/order`),
`PUT /summary`, `POST /publish`, `POST /discard`, `GET /preview`
and `GET /items/{itemId}/link` under `/api/projects/{id}/portal/content`; its answer adds `canManage`
(`client_portal:manage`) and each item's `uploadedBy`, and `canEdit` needs `canManage` too.

### Tables (migration `ProjectFiles`)
- `portal_items.for_client` (`tinyint(1)`, not null, default 0). Every row there before the migration is set to 1:
  those files were uploaded for the client.
- `project_file_comments`: project, item, author, body (up to 2,000), `created_at`, `edited_at`, `fixed_at`,
  `fixed_by_id`; a check that `fixed_at` and `fixed_by_id` are set together; `(item_id, id)` for a file's thread. The
  item's foreign key cascades (a never-published file is deleted with its comments); the others restrict.
- Seed: the permission `project_files:manage` (Design) for admins and super admins; `client_portal:manage` is taken off
  the admin role.
- **Rollback** drops the table and the column, and gives admins `client_portal:manage` back.

## Portal UX

### Hero images
A project's client portal has up to 12 hero images, in order; the client's page shows the first big and the others
beside it. The staff answer has `heroes` (each `{ fileId, name, url, urlExpiresAt }`, in order) and `hero`, the first or
null; the client's page has `heroes` and `hero` likewise (see [Preview and the client's page](#preview-and-the-clients-page)).

| Method and path (`/api/projects/{id}/portal/content`) | Purpose |
|---|---|
| `POST /heroes { itemId }` | Adds an image item's file at the end of the draft. Only JPEG, PNG or WebP (400 `portal_hero_not_image`); 409 `portal_hero_already_added` when it is one already; 422 `portal_hero_limit` past 12 |
| `DELETE /heroes/{fileId}` | Takes it off the draft (404 when it isn't on it) |
| `PUT /heroes/order { fileIds }` | The draft's new order: exactly its files, each once (409 `portal_order_stale` otherwise) |

- All three need `client_portal:manage` on a Design client project that isn't archived (422
  `portal_content_read_only`), take the content lock and move the revision on. They are `Never` for Proton.
- A hero can also be uploaded (`POST /api/projects/{id}/files/uploads` with `hero: true`, no section;
  `client_portal:manage`, client projects only): it goes at the end of the draft, within the same limit (checked when
  the upload starts and again when it completes).
- The hero changed (`changes.heroChanged`) when the draft list, images and order, differs from the published one: a
  new order put back as it was is nothing to publish. Publishing makes the published list the draft's; discarding the
  draft list the published one. Either way, a row whose image stays is kept and only takes its new place.
- `isHero` on an item (here and in Project files) says its file is one of the draft hero images.

### The client's project list
`GET /api/portal/me` (and the account in every sign-in answer) lists the projects newest first, by start date, then id.
Each project has, besides `id`, `title`, `status` and `unread` (unread chat messages and comments):

| Field | What it is |
|---|---|
| `heroUrl`, `heroUrlExpiresAt` | An hour-long inline link to the first published hero image, or null |
| `startedOn` | The project's start date |
| `lastPublishedAt` | The last publication, or null before the first |
| `lastActivityAt` | The latest of the last publication, the newest chat message and the newest comment (deleted ones aside), or null |
| `hasNews` | Something new: a publication since the client last opened the project (any publication, if they never opened it), or anything unread |

The list is read in one query (with the cover files in a second one); the unread counts are counted per project, as
before. The web client sorts by these fields itself.

### Tables (migration `PortalUx`)
- `portal_hero_images`: `project_id`, `file_id` (both restricting foreign keys), `sort_order`, `is_published`; unique
  `(project_id, is_published, file_id)`, and `(project_id, is_published, sort_order)` for each list in order. The
  migration copies each project's `portal_settings.hero_file_id` into a draft row and `published_hero_file_id` into a
  published row (both at `sort_order` 0), then drops those two columns and their foreign keys.
- `portal_messages`: `body` becomes optional and `deleted_at` is added, with the check
  `ck_portal_messages_body_unless_deleted` (only a deleted message has no text).
- `portal_project_access.last_opened_at` (`datetime(3)`, null until the client opens the project).
- **Rollback** puts the first image of each list back into the two `portal_settings` columns, drops the table, drops
  `deleted_at` and `last_opened_at` (a deleted message's text is gone for good: it comes back empty).

## Known limits (later phases)
- **Rate limiting:** the global per-user limit counts a client by IP. The portal token isn't the default scheme, so a
  signed-in client looks anonymous to the limiter (60 requests a minute per address). Discussion writes also have their
  own per-account limit.
- **Archived projects** still appear to their clients, read-only on both sides: the published page and the
  discussion can be read, but nothing can be changed, published or written.
- **Content activity** (uploads, renames, deletions) isn't in the activity feed: drafts are internal. Publishing is
  recorded (`portal_published`).
- **Client notifications** cover the discussion only (team messages, team comments, mentions). Publishing doesn't
  notify clients yet.
