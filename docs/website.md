# The company website

The company has a public website, a separate app on its own domain. It shows:
- the company's projects: a globe, a moving strip of up to 20 projects ("the conveyor"), a grid and a page for each;
- the services the company offers;
- its offices;
- its contact email and social links;
- a favicon.

All of it is edited on the **Website** page in the company area. The website reads it through a public API that needs no sign-in, and its contact form emails the company.

Code:
- `Controllers/WebsiteController.cs` and `Controllers/PublicWebsiteController.cs`;
- `Services/Website/`;
- `Business/Website/`;
- the tables `website_projects`, `website_project_images`, `website_services`, `website_locations` and `website_social_links`;
- the columns `contact_email` and `website_favicon_file_id` on `company_settings`.

## Who

| Permission | Lets you |
|---|---|
| `website:view` | See the Website page, read-only |
| `website:manage` | Change everything on it. Everything is public once saved |

Both are company permissions: super admins hold them, and admins don't unless granted one by hand.

The company email can also be changed with `company_settings:manage`, from Company settings.

## Projects

A website project is its own record. Everything on it may be public; nothing internal (money, hours, people) is ever read from the system.

- **Linking.** It can be linked to one system project, Design or Execution. A system project is shown at most once.
  - The form fills in the title, description and client from the linked project. They are copied once, and editable.
  - **Status and year keep following** the linked project while they're left empty (`null`). Typing one in overrides it; clearing it follows again.
  - A project with no link must have its own status and year.
- **Status.** The website shows only two:

  | System status | Website |
  |---|---|
  | `active`, `hold` | `in_progress` (public: `in-progress`) |
  | `completed`, `archived` | `completed` |

- **Year.** The completion year, else the target date's year, else the start year.
- **Type.** `villa`, `residential` or `hotel`, a fixed list for now.
- **Slug** (the page address, `/projects/{slug}`):
  - lowercase letters, digits and single dashes, at most 100 characters, unique;
  - left empty, it's made from the title (adding `-2`, `-3` … if taken) and then kept;
  - typing one that's taken gives `409 website_slug_taken`.
- **Publishing.** Only published projects reach the website. Publishing needs a status and a year (its own or the linked project's), a city, a country, a map position (both latitude and longitude), a description, a thumbnail and a main picture. Otherwise it's `422 website_project_incomplete`, with what's missing in the message. The form also gets the list as `missingToPublish`.
- **Pictures.**
  - Kinds:
    - the thumbnail: portrait 4:5, used in the strip and the grid;
    - the main picture (hero): wide, with a description and an optional credit;
    - a gallery: ordered, each picture with a description and an optional credit.
  - A credit is all of photographer, source and an https link, or nothing.
  - Pictures are uploaded with `POST /api/files` first, and must be photos (JPEG, PNG or WebP).
  - A replaced or removed picture is attached to nothing, so the file cleanup deletes it a day later ([files.md](files.md)).
- **The moving strip.**
  - `PUT /api/website/conveyor` sets the whole strip, in order: up to 20 projects, all published.
  - More than 20 is a `400`. A draft is `422 website_conveyor_invalid`.
  - Taking a project off the website, or deleting it, takes it out of the strip and closes the gap.

## Dashboard API (signed in)

| Endpoint | Permission | What |
|---|---|---|
| `GET /api/website/projects`, `GET …/{id}` | view or manage | Every project, newest first, as `{ projects, conveyorLimit, canManage }`. Each one has its own `status` and `year` (null while following), plus `shownStatus` and `shownYear`, `linkedProject`, `gallery` and `missingToPublish` |
| `POST /api/website/projects`, `PUT …/{id}`, `DELETE …/{id}` | manage | The whole form each time. Pictures are `thumbnailFileId` and `heroFileId`: the current id keeps it, a new upload replaces it, `null` clears it |
| `PUT /api/website/conveyor` | manage | `{ projectIds }` in order |
| `POST /api/website/projects/{id}/images` | manage | `{ fileId, alt, credit? }`, added at the end |
| `PUT …/images/{imageId}`, `DELETE …/images/{imageId}` | manage | `{ alt, credit? }` |
| `PUT …/images/order` | manage | `{ ids }`: every picture once. If one was added or removed meanwhile: `409 website_order_stale` |
| `GET /api/website/link-candidates?search=` | manage | Up to 20 system projects of both businesses, with what the form fills in and `websiteProjectId` if one is already shown |
| `GET /api/website/content` | view or manage | `{ services, locations, socialLinks, contactEmail, faviconFileId, canManage }` |
| `POST /api/website/services`, `PUT …/{id}`, `DELETE …/{id}`, `PUT …/order` | manage | `{ title, description }`, plain text. Each answers with the whole content |
| `POST /api/website/locations`, `PUT …`, `DELETE …`, `PUT …/order` | manage | `{ city, country, latitude, longitude, address, phone }` |
| `POST /api/website/social-links`, `PUT …`, `DELETE …`, `PUT …/order` | manage | `{ label, url }`. The URL must be https |
| `PUT /api/website/favicon`, `DELETE …` | manage | `{ fileId }`: PNG, JPEG or WebP (no SVG: it can carry scripts) |
| `PUT /api/company/contact-email` | `company_settings:manage` or `website:manage` | `{ email }`. Empty clears it. It is also `contactEmail` on `GET /api/company/settings` |

## Public API (no sign-in)

Under `/api/public/website`. JSON is camelCase, and lists come in display order. Picture addresses are paths on this API (`/api/public/website/files/{id}`); a website on another origin prefixes them with the API's address.

| Endpoint | Answer |
|---|---|
| `GET /projects` | Every published project (the website filters the grid itself) |
| `GET /projects/{slug}` | One published project, else `404` |
| `GET /services` | `[{ id, title, description }]` |
| `GET /locations` | `[{ id, city, country, lat, lng, address, phone }]` |
| `GET /contact` | `{ email }`: null until the company sets one |
| `GET /social-links` | `[{ id, label, url }]` |
| `GET /settings` | `{ faviconUrl }`: null until one is chosen |
| `GET /files/{id}` | A picture the website shows: a published project's, or the favicon. Anything else is `404` |
| `POST /contact-messages` | `{ name, email, phone?, message, website }`. `202` once emailed |

A project:

```json
{
  "id": 12, "slug": "villa-rizk", "title": "Villa Rizk",
  "type": "villa", "status": "completed", "client": "The Rizk family",
  "location": { "city": "Beirut", "country": "Lebanon", "lat": 33.8938, "lng": 35.5018 },
  "year": 2022, "description": "Plain text.",
  "thumbnail": "/api/public/website/files/40",
  "hero": { "src": "/api/public/website/files/41", "alt": "The villa at dusk",
            "credit": { "photographer": "…", "source": "…", "url": "https://…" } },
  "gallery": [{ "src": "/api/public/website/files/42", "alt": "The pool", "credit": null }],
  "showInConveyor": true, "conveyorOrder": 1
}
```

- `client` is `""` when none is named.
- `credit` is `null` when the picture has none.
- `conveyorOrder` is `null` outside the strip.

**Caching.** Answers may be cached for a minute (`Cache-Control: public, max-age=60`), and pictures for a day: a picture's address never changes its content, since a new picture is a new file.

**Rate limits.**
- Every request under `/api/public/website` has its own bucket: 600 a minute per IP address (`RateLimiting:PublicWebsitePermitsPerMinute`). A page loads every picture through the API, so the 60-a-minute anonymous limit would be too tight.
- The contact form is also limited to 5 messages an hour per IP address (`RateLimiting:WebsiteContactPermitsPerHour`), failed ones included.

## The contact form

The website's form sends its fields to `POST /contact-messages`. The rules are checked again here:

| Field | Rule |
|---|---|
| `name` | Required, up to 120 characters, no line breaks |
| `email` | Required, one plain address |
| `phone` | Optional. Digits, spaces, `( ) . -` and a leading `+`, at least 6 digits |
| `message` | 10 to 4,000 characters |

- **Validation errors** come back as `400 validation_failed`, keyed `name`, `email`, `phone` and `message`.
- **`website`** is a field visitors never see. Anything in it means a robot filled the form: the answer is still `202`, and nothing is sent.
- **The email** goes to the company's contact email. Replying answers the visitor, because the email's reply-to is their address. Everything they wrote is HTML-encoded, and nothing is stored or echoed back.
- **Failures:**
  - With no contact email set, the answer is `503 website_contact_unavailable`.
  - If the mail server fails, it's `503 email_delivery_failed`, and the failure is logged.

## Not done yet

- **Deployment.** Serving the website and its domain isn't set up.
- **CORS.** A website on another origin needs its origin allowed. Same-origin needs nothing.
- **Editable project types.** They would become `{ key, label }`.
