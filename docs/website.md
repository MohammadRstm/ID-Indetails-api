# The company website

The company has a public website, a separate app on its own domain. It shows:
- the company's projects: a globe, a moving strip of up to 20 projects ("the conveyor"), a grid and a page for each;
- the services the company offers;
- its offices;
- its contact email and social links;
- a favicon.

All of it is edited in the app's **Website** area. The website reads it through a public API that needs no sign-in, and its contact form emails the company. The Website area also shows the website's visitors, read from Google Analytics ([Analytics](#analytics)).

Code:
- `Controllers/WebsiteController.cs`, `Controllers/PublicWebsiteController.cs` and `Controllers/WebsiteAnalyticsController.cs`;
- `Services/Website/`, and `Services/GoogleAnalytics/` for Google Analytics;
- `Business/Website/`;
- the tables `website_projects`, `website_project_images`, `website_services`, `website_locations` and `website_social_links`;
- the columns `contact_email`, `website_favicon_file_id` and `website_under_construction_since` on `company_settings`.

## Who

| Permission | Lets you |
|---|---|
| `website:view` | See the Website area, read-only |
| `website:manage` | Change everything in it. Everything is public once saved |
| `website_analytics:view` | See the website's visitors, from Google Analytics ([Analytics](#analytics)) |

## The Website domain

The website is a domain of its own, beside Design and Execution, but a **service, not a business**: it has no projects, clients or money (`DomainCodes.Website`; `DomainCodes.Businesses` are the other two).
- **Its staff** are admins whose home domain is `website` (it has no employees: inviting one, or making a website admin an employee, is refused with `403 invitation_not_allowed` or `422 not_for_this_domain`).
  - Their defaults are only the permissions limited to the website: `website:view`, `website:manage` and `website_analytics:view`. No project, client, team or Proton permission.
  - They see only the website (`visibleDomains: ["website"]`), and the app opens the Website area for them straight after sign-in.
  - They are paid a fixed salary like any admin, so their pay is a company cost in the company overview.
  - They aren't moved: moving is between Design and Execution only.
- **Anyone else** given `website:view`, `website:manage` or `website_analytics:view` keeps their business and also sees the website (`visibleDomains` gains `website`), the way one permission of the other business opens it. Their "only one business, so no need to choose" shortcuts (new projects, clients, teams, invitations) still look at their businesses only.
- **Super admins** hold all three, as ever.
- The businesses' pages (dashboard, finance, billing settings, domain settings) answer `404` for `website`, and projects and clients can't be created in it.
- **Linking a project** (`GET /api/website/link-candidates`) searches only the projects of the businesses the caller can see: the website's own staff type their projects in.

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
  - Always made from the project's name: accents dropped, lowercase ASCII letters and digits joined by single dashes, at most 100 characters.
  - A name must have at least one Latin letter or digit (`400`).
  - The same name again is numbered: `villa-rizk`, then `villa-rizk-2`.
  - It follows the name: a rename gives a new address, and the old one answers `404`.
- **Place.** Coordinates, or a country with or without a city, from the lists (see [Places](#places)). A published project always has a country and a point; the city may be null.
- **Publishing.** Only published projects reach the website. Publishing needs:
  - a status and a year (its own or the linked project's);
  - a country and a map position;
  - a description, a card picture and a page picture.

  Otherwise it's `422 website_project_incomplete`, with what's missing in the message, or `422 website_place_not_found` when the place isn't one of the lists'. The form also gets the list as `missingToPublish`.
- **Pictures.** Each has one use:

  | Picture | Shape | Where the website shows it |
  |---|---|---|
  | **Card picture** (`thumbnail`) | Tall 4:5, about 720 × 900 | Only on the project's card: in the moving strip and in the Projects grid |
  | **Page picture** (`hero`) | Wide 16:9, about 1920 × 1080 | Only across the top of the project's own page, beside the globe |
  | **Gallery** | Wide 3:2 works best | Further down the project's own page, in order |

  - Each picture has a description (`alt`), read out to people who can't see it.
  - Pictures are uploaded with `POST /api/files` first, and must be photos (JPEG, PNG or WebP).
  - A replaced or removed picture is attached to nothing, so the file cleanup deletes it a day later ([files.md](files.md)).
- **The moving strip.**
  - `PUT /api/website/conveyor` sets the whole strip, in order: up to 20 projects, all published.
  - More than 20 is a `400`. A draft is `422 website_conveyor_invalid`.
  - Taking a project off the website, or deleting it, takes it out of the strip and closes the gap.

## Places

**The lists.** The country and city are chosen from lists, so a place is never misspelt and always has a point:
- `GET /api/website/places/countries` returns `[{ code, name }]`: ISO 3166-1 codes, with the English names .NET knows.
- `GET /api/website/places/cities?country=LB&search=jb` returns up to 50 of the country's cities, biggest first. Those starting with the typed text come first, then those containing it, with accents and case ignored. Each has `{ name, latitude, longitude, population }`.
- `GET /api/website/places/cities-elsewhere?country=CD&search=brazzaville` returns up to 5 cities of that name in other countries, `[{ name, countryCode, countryName }]`, from three letters typed: an exact name first, then the biggest. The city picker shows them when the chosen country has no match ("Brazzaville is in Congo - Brazzaville"), so a place isn't saved under the wrong country.
- Both read GeoNames' places of 1,000 people or more (https://www.geonames.org, **CC BY 4.0**), trimmed into `Assets/geonames-cities.tsv.gz` and built into the API (`GeoNamesDirectory`).
- **Every village of the countries the company works in:** their places come from GeoNames' own country files, not only those of 1,000 people. Abandoned, historical and destroyed places are left out.
  - Lebanon (`LB.zip`): about 3,700.
  - Ghana (`GH.zip`): about 16,000.
  - The Republic of the Congo, Brazzaville (`CG.zip`): about 5,800.
  - Liberia (`LR.zip`): about 6,300.
  - Sierra Leone (`SL.zip`): about 14,400.
- The list holds about 217,000 places (3.4 MB).
- Those places can also be found by the other names people use: "Jbeil" finds Byblos, and "Gemmayze" finds Ej Jimmaïze. Up to 24 Latin-script names are kept per place, and a place is always shown by its main name.
- To refresh it, run `python -I Assets/build-geonames-cities.py cities1000.zip LB.zip GH.zip CG.zip LR.zip SL.zip` with GeoNames' files. Any other country's file can be added the same way. A checkout without the list still builds and runs, with empty lists.
- Names are matched whatever the case and accents ("saida" finds "Saïda") and kept as the list spells them. When a country has two places of the same name, the bigger one is meant.

**Placing a project or an office** (`WebsitePlaceResolver`):
1. **Coordinates** (more accurate) always win, and go on the globe exactly as typed. A missing country is the nearest town's (within 150 km). A missing city is the nearest town's in that country, when one is within 30 km; otherwise there's no city.
2. **A city:** the point is picked within about 1 km of it.
3. **A country alone** (projects only; an office needs its city): the point is picked within about 1 km of one of the country's towns, at random, and the city stays null.

A picked point (`positionApproximate: true`):
- No other project or office has that exact point.
- It's checked with the map service: one that falls in the sea or another country is picked again, up to 5 times. After that the town's own point is used.
- It's chosen once and kept. It's chosen again only when the city or country changes, and replaced as soon as real coordinates are typed in. A form that sends the point back unchanged keeps it.

**The map service: OpenStreetMap's Nominatim** (`Services/Geocoding`):
- It's only asked which country a picked point is in.
- **Their usage policy:** the API names itself (`Geocoding:UserAgent`), makes at most one request a second, and never uses it for search-as-you-type.
- **Settings:** `Geocoding:Enabled`, `Geocoding:BaseUrl`, `Geocoding:UserAgent` and `Geocoding:TimeoutSeconds` (5 by default).
- When the service is down, the town's own point is used.

**When a place isn't one of the lists':**
- A project draft still saves, with what could be worked out.
- Publishing is `422 website_place_not_found`, with a message saying what to choose.
- An office is public once saved, so it's refused the same way.

The integration tests use `FakeGeocoder` and a few towns (`TestPlaces`), and never call the internet.

## Dashboard API (signed in)

| Endpoint | Permission | What |
|---|---|---|
| `GET /api/website/projects`, `GET …/{id}` | view or manage | Every project, newest first, as `{ projects, conveyorLimit, canManage }`. Each one has its own `status` and `year` (null while following), plus `shownStatus` and `shownYear`, `linkedProject`, `gallery` and `missingToPublish` |
| `POST /api/website/projects`, `PUT …/{id}`, `DELETE …/{id}` | manage | The whole form each time. Pictures are `thumbnailFileId` and `heroFileId`: the current id keeps it, a new upload replaces it, `null` clears it |
| `PUT /api/website/conveyor` | manage | `{ projectIds }` in order |
| `POST /api/website/projects/{id}/images` | manage | `{ fileId, alt }`, added at the end |
| `PUT …/images/{imageId}`, `DELETE …/images/{imageId}` | manage | `{ alt }` |
| `PUT …/images/order` | manage | `{ ids }`: every picture once. If one was added or removed meanwhile: `409 website_order_stale` |
| `GET /api/website/link-candidates?search=` | manage | Up to 20 system projects of both businesses, with what the form fills in and `websiteProjectId` if one is already shown |
| `GET /api/website/content` | view or manage | `{ services, locations, socialLinks, contactEmail, faviconFileId, underConstructionSince, canManage }` |
| `POST /api/website/services`, `PUT …/{id}`, `DELETE …/{id}`, `PUT …/order` | manage | `{ title, description }`, plain text. Each answers with the whole content |
| `POST /api/website/locations`, `PUT …`, `DELETE …`, `PUT …/order` | manage | `{ city?, country?, latitude?, longitude?, address, phone }`: the coordinates, or the country and city from the lists ([Places](#places)) |
| `GET /api/website/places/countries`, `GET /api/website/places/cities?country=&search=`, `GET /api/website/places/cities-elsewhere?country=&search=` | view or manage | The lists ([Places](#places)) |
| `POST /api/website/social-links`, `PUT …`, `DELETE …`, `PUT …/order` | manage | `{ label, url }`. The URL must be https |
| `PUT /api/website/favicon`, `DELETE …` | manage | `{ fileId }`: PNG, JPEG or WebP (no SVG: it can carry scripts) |
| `PUT /api/website/under-construction` | manage | `{ underConstruction }`. Puts the website under construction or brings it back ([below](#under-construction)) |
| `PUT /api/company/contact-email` | `company_settings:manage` or `website:manage` | `{ email }`. Empty clears it. It is also `contactEmail` on `GET /api/company/settings` |

## Public API (no sign-in)

Under `/api/public/website`. JSON is camelCase, and lists come in display order. Picture addresses are paths on this API (`/api/public/website/files/{id}`); a website on another origin prefixes them with the API's address.

| Endpoint | Answer |
|---|---|
| `GET /projects` | Every published project (the website filters the grid itself) |
| `GET /projects/{slug}` | One published project, else `404` |
| `GET /services` | `[{ id, title, description }]` |
| `GET /locations` | `[{ id, city, country, countryCode, lat, lng, address, phone }]` |
| `GET /contact` | `{ email }`: null until the company sets one |
| `GET /social-links` | `[{ id, label, url }]` |
| `GET /settings` | `{ faviconUrl, underConstruction }`: `faviconUrl` is null until one is chosen |
| `GET /files/{id}` | A picture the website shows: a published project's, or the favicon. Anything else is `404` |
| `POST /contact-messages` | `{ name, email, phone?, message, website }`. `202` once emailed |

A project:

```json
{
  "id": 12, "slug": "villa-rizk", "title": "Villa Rizk",
  "type": "villa", "status": "completed", "client": "The Rizk family",
  "location": { "city": "Beirut", "country": "Lebanon", "countryCode": "LB", "lat": 33.8938, "lng": 35.5018, "approximate": false },
  "year": 2022, "description": "Plain text.",
  "thumbnail": "/api/public/website/files/40",
  "hero": { "src": "/api/public/website/files/41", "alt": "The villa at dusk" },
  "gallery": [{ "src": "/api/public/website/files/42", "alt": "The pool" }],
  "showInConveyor": true, "conveyorOrder": 1
}
```

- `client` is `""` when none is named.
- `location.city` is null for a project placed in its country alone. `lat` and `lng` are always set; `approximate` is true when the point was picked ([Places](#places)).
- `conveyorOrder` is `null` outside the strip.

**Caching.** Answers may be cached for a minute (`Cache-Control: public, max-age=60`), and pictures for a day: a picture's address never changes its content, since a new picture is a new file.

**Rate limits.**
- Every request under `/api/public/website` has its own bucket: 600 a minute per IP address (`RateLimiting:PublicWebsitePermitsPerMinute`). A page loads every picture through the API, so the 60-a-minute anonymous limit would be too tight.
- The contact form is also limited to 5 messages an hour per IP address (`RateLimiting:WebsiteContactPermitsPerHour`), failed ones included.

## Under construction

The Website area's **Site** page can put the website under construction, after a confirmation, and bring it back the same way.
- **While it's on,** the public settings answer `underConstruction: true`. The website then shows only its construction page, which links to Contact, and its Contact and Privacy pages.
- **The API keeps answering:** every public endpoint still works, and the contact form most of all, since the construction page sends visitors to it. Which pages to show is the website's decision.
- **Since when:** `company_settings.website_under_construction_since` holds when it was switched on; null means the website is live. Switching it on again keeps the first date.
- **Timing:** the settings answer is cached for 60 seconds, so a switch shows on the website within a minute.

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
- **Until a contact email is set**, it goes to `WebsiteContact:FallbackAddress` instead (`WEBSITE_CONTACT_FALLBACK_TO` in the server's `.env`; the developer's address by default). That address is never shown on the website: `GET /contact` stays `null`.
- **Failures:**
  - With neither set, the answer is `503 website_contact_unavailable`.
  - If the mail server fails, it's `503 email_delivery_failed`, and the failure is logged.

## Analytics

The website sends its visits to Google Analytics 4, only for visitors who accept its cookie question (the website's README, "Analytics"). The API reads them back with Google's **Data API** for the Website area's Analytics page. Only totals are read, never anything about one visitor.

**Who:** `website_analytics:view`. By default the website's staff and super admins hold it; anyone else is given it from their profile. `website:view` alone doesn't show the analytics.

**How the API reads them** (`Services/GoogleAnalytics/`):
- As a Google Cloud **service account** with **Viewer** access to the GA4 property and the read-only scope (`analytics.readonly`), through Google's `Google.Apis.AnalyticsData.v1beta` package. Only `GoogleAnalyticsClient` talks to Google; the tests use `FakeGoogleAnalytics` and never call it.
- A period is two batches of reports (Google takes at most five per batch): totals, days, channels, sources and pages; then countries, devices, new and returning visitors, and events. Each is one of Google Analytics' own standard reports, and columns are read by name.
- The answers are the same for everyone allowed to see them and hold nothing of any one user, so they are cached for all alike: **15 minutes** for a period ending in the last week (Google may still add to those days), **6 hours** for an older one, **1 minute** for the last 30 minutes.
- **Nothing is retried.** Google counts server errors against a quota of 10 an hour, so after any failure the API leaves Google alone for a minute and answers `503 website_analytics_unavailable`. The reason is in the log: no Viewer access or the API not enabled (`403`), out of quota (`429`), a deleted key, or no answer within `GoogleAnalytics:TimeoutSeconds` (10). A low hourly quota is logged too.
- Days follow the **property's time zone**: set it to the company's (Asia/Beirut), so that "yesterday" is the same day in both. The answer carries `timeZone`.

| Endpoint | What |
|---|---|
| `GET /api/website/analytics?from=&to=&compare=` | The period's `totals` (`visitors` are GA's active users, `engagementRate` a fraction, `averageEngagementSeconds` per visitor, `contactRequests` the `generate_lead` events), every day of it, `channels`, `sources` (top 25), `pages` (top 50; a `/projects/{slug}` page has its website project's `projectTitle` while it has that address), `countries`, `devices`, `visitorTypes` (new, returning) and `events`. Both dates or neither: the 28 days ending yesterday by default, at most 366 days, ending today at the latest. With `compare` (the default), `previousTotals`, each day's `previous…` and each event's `previousCount` are for the period of the same length before. `hasData` is false until Google has recorded something; `thresholded` is true when Google left out rows with very few visitors |
| `GET /api/website/analytics/realtime` | The last 30 minutes, a few minutes behind at most: `visitorsNow`, `visitorsPerMinute` (30, oldest first), the `pages` being read (by title: realtime reports have no addresses) and the `countries` |

Both answer `503 website_analytics_not_configured` while Google Analytics isn't switched on, and allow 30 requests a minute per person (`RateLimiting:WebsiteAnalyticsPermitsPerMinute`).

**Not in Google Analytics' Data API, so not here:** Search Console's search queries (they need its own API), Google Ads costs (they need an Ads account linked), and anything about one visitor.

### Setting it up

1. **Google Analytics** (Admin):
   - Property details: the **property ID** (a number), and the reporting time zone, **Asia/Beirut**.
   - Data streams, the website's stream: its **measurement ID** (`G-…`) goes to the website's build (its README). In Enhanced measurement, Page views, Show advanced settings: **untick "Page changes based on browser history events"**. The website sends its own page views; left on, every search typed on the grid would count as page views. Also untick "Site search" and "Form interactions", and keep "Outbound clicks" and "Scrolls".
   - The same stream, Redact data: email addresses, and the query parameter `q`.
   - Data collection: Google signals off. Data retention: 14 months.
   - Events: mark `generate_lead`, `email_click` and `phone_click` as **key events** once each has happened, or create them as key events by name.
2. **Google Cloud:** in a project, enable the **Google Analytics Data API**. Create a **service account** (it needs no role in the project) and a **JSON key** for it. If key creation is refused, the organisation's policy `iam.managed.disableServiceAccountKeyCreation` is blocking it; an organisation admin can lift it.
3. **Google Analytics again** (Admin, Property access management): add the service account's email as a **Viewer**.
4. **The API:** base64 the key on one line, then delete the key file.
   - Locally (cmd): `powershell -NoProfile -Command "[Convert]::ToBase64String([IO.File]::ReadAllBytes('key.json'))"`, then `dotnet user-secrets set "GoogleAnalytics:ServiceAccountKeyBase64" "<that>" --project ID-Indetails.api`, and the same for `GoogleAnalytics:PropertyId` (the number) and `GoogleAnalytics:Enabled` (`true`).
   - On the server: `base64 -w0 key.json` into `GA_SERVICE_ACCOUNT_KEY_BASE64` in `.env`, with `GA_PROPERTY_ID` and `GA_ENABLED=true`, then `docker compose -f docker-compose.prod.yml up -d api` (or run Deploy).
   - With `Enabled` true, the API doesn't start without a numeric property ID and a service account's key.
5. **Changing the key:** create a new one, put it in `.env`, restart the API as above, then delete the old key in Google Cloud.

## Not done yet

- **Going live.** The website is in the production stack ([deployment.md](deployment.md#the-public-website)); it goes
  live once its domain resolves and `WEBSITE_DOMAIN` is set. Its nginx proxies `/api/public/website/` on the same
  origin, so no CORS is needed.
- **Editable project types.** They would become `{ key, label }`.
