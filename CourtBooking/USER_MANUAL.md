# CourtBook — User Manual

A complete guide for facility owners and customers using the CourtBook online court reservation platform.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Account Types](#2-account-types)
3. [Quick Start for Facility Owners](#3-quick-start-for-facility-owners)
4. [Quick Start for Customers](#4-quick-start-for-customers)
5. [Facility Owner Guide](#5-facility-owner-guide)
6. [Customer Guide](#6-customer-guide)
7. [Payments](#7-payments)
8. [Subscription — Free Trial and Pro](#8-subscription--free-trial-and-pro)
9. [Custom Branding (Pro)](#9-custom-branding-pro)
10. [Frequently Asked Questions](#10-frequently-asked-questions)
11. [Support](#11-support)

> **What's new:** Guest booking (no account required), Open Play public sign-ups, bundled multi-court packages, a recurring weekly schedule with rate tiers, multiple instant payment methods, and times now shown in 12-hour format (AM/PM) everywhere. See the relevant sections below.

---

## 1. Overview

CourtBook is a multi-tenant online booking platform for sports facilities (badminton, tennis, basketball, pickleball, futsal, volleyball, swimming, billiards, table tennis, football). Each facility owner gets their own private workspace, a shareable booking link, and a customer-facing courts page.

**Key concepts:**

- **Facility** — a sports venue owned by one admin account. Each facility has its own courts, time slots, payment details, and customer base.
- **Court** — a single bookable resource (e.g. *Badminton Court 1*) belonging to a facility.
- **Time slot** — a specific bookable window on a specific date (e.g. *June 5, 8:00–9:00 AM on Court 1*).
- **Booking** — a customer's reservation of a time slot, awaiting payment and admin confirmation.
- **Bundle** — a flat-priced package of two or more courts sold together for a recurring time window (e.g. *Courts 1 + 2, weekday evenings, ₱800 flat*).
- **Open Play** — an hour range you host and open to the public, where individual players sign up for a spot (not the whole court) at a set price per head, up to a maximum number of players.
- **Guest booking** — a reservation made without creating an account. The guest manages it later via a private link emailed to them.
- **Shareable URL** — your unique `/sportshub/your-slug` link that customers visit to view and book your courts.

---

## 2. Account Types

| Role | Who | Sees |
|---|---|---|
| **Admin** (Facility Owner) | The person managing a sports facility | Their own admin dashboard, only their courts and bookings |
| **Customer** | Registered players who want to book courts | The facility page they were invited to, their own bookings |
| **Guest** | Players who book or join Open Play **without registering** | Only the specific booking they made, via a private emailed link — no dashboard, no password |

A customer can only belong to **one preferred facility** at a time. The facility is locked in the first time they visit a shared link, register, or log in from that link.

A guest isn't tied to a facility at all — each booking is self-contained and reached only through its own link. If a guest later registers with the same email, their booking history isn't automatically merged.

---

## 3. Quick Start for Facility Owners

1. **Sign up** — go to the app home page and choose **Start Free Trial**.
2. **Fill in your details** — first name, last name, email, phone number, password.
3. **Activate trial** — you get 30 days of full access immediately.
4. **Go to Admin → Settings** and fill in:
   - Facility Name
   - Address
   - URL Slug (e.g. `greenfield-sports`)
   - GCash and/or Maya payment details
   - Payment Instructions (optional)
   - (Optional) Your PayMongo secret key and the instant payment methods you want to offer
5. **Add courts** — Admin → Courts → *Add Court*. Set name, sport, hours, price per hour, indoor/outdoor.
6. **Generate time slots** — Admin → Courts → *Manage Slots* on each court to create bookable windows.
7. **(Optional) Set a recurring weekly schedule** — Admin → Courts → *Default Schedule & Rates* to add day-of-week rate tiers, reserve hours for Open Play, or mark public holidays.
8. **(Optional) Create a bundle** — Admin → Bundles to sell two or more courts together as a flat-priced package.
9. **Share your link** — Admin → Settings → copy the **Shareable Booking URL** and send it to your customers.

That's it. Customers can now visit your link and book — with or without creating an account.

---

## 4. Quick Start for Customers

1. **Click the link** your facility owner sent you (e.g. `https://courtbooksolutions.org/sportshub/greenfield-sports`).
2. **Browse courts** — pick the sport you want and tap a court card.
3. **Pick a date and time slot**.
4. **Sign up, log in, or continue as a guest** — no account is required; see [6.6 Booking Without an Account (Guest Checkout)](#66-booking-without-an-account-guest-checkout).
5. **Confirm the booking** and you'll be sent to the **Payment** page.
6. **Pay instantly by card/e-wallet** (if the facility has it enabled) or **send payment** via GCash or Maya to the number shown.
7. **Submit your proof** (manual payment only) — enter the reference number (and optionally upload a screenshot).
8. **Wait for confirmation** — the facility admin will verify your payment, usually within a few hours (instant payments confirm automatically).
9. **Check My Bookings** to see the status — guests use the link emailed to them instead.

---

## 5. Facility Owner Guide

### 5.1 Admin Dashboard

Navigate to **Admin** from the top navigation. The dashboard shows:

- Total courts, active bookings, pending payments
- Recent bookings list
- Quick links to Courts, Bookings, Settings, Subscription

### 5.2 Settings

**Admin → Settings** is the control panel for your public-facing details.

#### Facility Info

- **Facility Name** — appears in booking confirmations, payment pages, and the navbar (if no logo is set).
- **Address** — shown to customers on the facility page, booking page, and payment summary. Helps them know where to go.
- **URL Slug** — the unique part of your shareable link (`/sportshub/your-slug`). Lowercase letters, numbers, and hyphens only.
  > ⚠ Changing your slug will break any links you've already shared.

#### Payment Methods

**Manual (GCash / Maya transfer + screenshot proof):**

- **GCash Number / Name** — the number customers will send money to.
- **Maya Number / Name** — alternative payment method.
- **Payment Instructions** — free-form text shown on the payment page (e.g. *"Send the exact amount and include your booking ID in the notes."*).

You can fill in either GCash, Maya, or both. If neither is filled in — and you haven't set up instant payment either — customers see a warning to contact you directly.

**Instant (PayMongo — card, e-wallets, online banking):**

- **PayMongo Secret Key** — from your [PayMongo dashboard](https://dashboard.paymongo.com/developers) → Developers → API Keys. Once set, customers see a **Pay Instantly** option and their booking is confirmed automatically the moment payment succeeds — no manual verification needed.
- **Payment Methods Offered to Customers** — tick every method you've actually activated on your PayMongo dashboard: Visa/Mastercard, GCash, Maya, GrabPay, QRPh, Online Banking, BillEase. QRPh is ticked by default since it works for most merchants without extra activation.
  > ⚠ Only tick a method here if it's also turned on in your PayMongo account — otherwise customers may not actually see it at PayMongo's checkout, regardless of what's ticked in CourtBook.

You can offer manual payment, instant payment, or both at the same time — customers will see whichever options you've configured.

#### Shareable Booking URL

Once you set a slug, your shareable URL is shown with a **Copy** button. Use this to share with customers via SMS, email, social media, or print it on a poster.

### 5.3 Managing Courts

**Admin → Courts** lists all courts you own.

#### Adding a court

Click **Add Court** and fill in:

- **Name** — e.g. *Court 1*, *Tennis Court A*
- **Sport** — choose from the list
- **Description** — short blurb shown on the court card
- **Image URL** (optional) — header image for the card
- **Indoor / Outdoor**
- **Opening Hour / Closing Hour** — pick from a dropdown shown in 12-hour format (e.g. *6:00 AM* to *10:00 PM*)
- **Price Per Hour** — in PHP

#### Editing or deactivating a court

Use **Edit** to change details, or **Toggle Active** to hide a court without deleting it. Deactivated courts are hidden from customers but historical bookings are preserved.

#### Managing time slots

Click **Manage Slots** on a court. You can:

- **Generate slots** for a date range (defines bookable windows of N hours each)
- **Delete a slot** to remove that window from availability
- **Toggle a slot** active/inactive

If you don't generate any time slots, the system falls back to hourly slots between the court's opening and closing hours.

#### Default Schedule & Rates

Click **Default Schedule & Rates** on a court to set a recurring *weekly* default — no need to re-enter it every week. Date-specific slots and blocks (above) always override this default when both apply.

- **Rate tiers** — set a different price per hour for specific days of the week and hour ranges (e.g. *weekends, 6–10 PM, ₱500/hr* vs. the court's normal rate). Add as many tiers as you need; you can also include holidays in a tier.
- **Open Play blocks** — reserve a day-of-week + hour range as **Admin-Hosted Open Play** instead of regular hourly booking. Turn on **Allow Public Sign-up** to let individual players join (see [Open Play Sign-ups](#55-open-play-sign-ups) below) by setting a **Max Players** count and a **Price Per Head**.
- **Holidays** — mark specific dates as holidays under Admin → Settings; any rate tier or schedule block with "+ Holidays" ticked also applies on those dates, even if they fall on a normal weekday.

### 5.4 Bundled Court Packages

**Admin → Bundles** lets you sell two or more courts together as a single flat-priced package (e.g. *Courts 1 + 2 together, weekday evenings, ₱800 flat* instead of booking each separately).

1. **Create a bundle** — give it a name and pick the member courts.
2. **Add Peak Windows** — on the bundle's page, add recurring day-of-week + hour ranges (optionally including holidays) with a flat price for the whole package.
3. Once a window is added, customers booking any member court during that window are offered the bundle option automatically. Some windows can be marked **Bundle Only**, meaning that hour range isn't bookable as an individual court at all during that time — only as part of the bundle.

A bundle purchase creates one linked booking per member court, sharing the same total split evenly, so each court's booking still shows correctly in **Admin → Bookings**.

### 5.5 Open Play Sign-ups

Once a court has an Open Play block with **Allow Public Sign-up** turned on (see [Default Schedule & Rates](#default-schedule--rates)), customers can join that session directly from the court's availability grid without booking the whole court.

**Admin → Courts → Open Play Sign-ups** shows a roster grouped by session (court, date, hour range), with:

- Headcount vs. the max you set (e.g. *5 of 8 spots reserved*)
- Each signed-up player's name, spot count, and total price
- **Payment column** — method, reference number, and a **View Screenshot** link to check the uploaded proof before deciding
- **Confirm** / **Reject** buttons for sign-ups awaiting confirmation

A pending-confirmation badge for Open Play appears on the Admin dashboard, the Courts list, and the Open Play Sign-ups page itself, so a submitted payment is never missed.

### 5.6 Managing Bookings

**Admin → Bookings ("All Bookings")** lists every reservation made on your courts — regular bookings, bundle bookings, **and confirmed/any-status Open Play sign-ups**, all in one table. Filter by status or date to narrow it down. A row from Open Play is marked with an **"Open Play · N spot(s)"** badge next to the court name.

A **Guest** badge appears next to the customer's name for bookings made without an account (see [6.6 Booking Without an Account](#66-booking-without-an-account-guest-checkout)).

For each booking you can:

- **View payment proof** — see the screenshot and reference number the customer submitted
- **Confirm Payment** — marks the booking as paid and confirmed
- **Reject Payment** — sends the booking back to *Pending* with a note for the customer
- **Update Status** — Pending → Confirmed → Completed, or Cancel (works for Open Play rows too)

> 🔒 You can only see and act on bookings for courts you own. Other facilities' bookings are not visible.

> Note: the **Awaiting Confirmation** tab on this page only lists regular/bundle bookings still awaiting payment review — pending Open Play sign-ups are reviewed on their own [Open Play Sign-ups](#55-open-play-sign-ups) page instead, which has its own pending-count badge.

### 5.7 Payment Verification Workflow

1. Customer submits a booking → status `Pending Payment`.
2. Customer pays via GCash/Maya (or instantly by card/e-wallet if you've enabled PayMongo) → submits reference + screenshot → status `Awaiting Verification` (instant payments confirm automatically, skipping this step).
3. **You** check your GCash/Maya app for the matching reference number.
4. If valid → click **Confirm Payment**. Booking is now `Confirmed`.
5. If invalid or duplicate → click **Reject Payment**. Customer is notified and can resubmit.

---

## 6. Customer Guide

### 6.1 Finding Your Facility

The first time you click a `/sportshub/your-slug` link, the facility is remembered for 7 days via a cookie. Once you register or log in, the facility becomes **permanently associated** with your account — every subsequent login takes you straight to that facility's courts.

If you ever need to switch to a different facility, simply click a new `/sportshub/different-slug` link.

### 6.2 Browsing Courts

The facility page shows all active courts. You can filter by sport using the chips at the top. Each court card shows:

- Court name and sport
- Indoor/Outdoor badge
- Opening hours
- Price per hour

Click **Book** to open the court's availability calendar.

### 6.3 Booking a Court

1. **Pick a date** from the date picker (up to 30 days ahead).
2. **Pick an available slot** — green = available, red = already booked. A slot may instead show as **Open Play** (join as an individual, see [6.7](#67-joining-open-play)) or **Bundle Only** (only bookable as part of a package, see [6.8](#68-booking-a-bundle)) if the facility has set it up that way.
3. **Confirm details** on the booking form (notes optional).
4. **Submit** — you'll be redirected to the Payment page.

### 6.4 My Bookings

Click **My Bookings** in the navbar to see all your reservations across statuses:

- **Pending Payment** — you haven't paid yet, click *Pay Now*
- **Awaiting Verification** — you've submitted proof, waiting for the facility
- **Confirmed** — paid and approved, show up at the court!
- **Cancelled / Rejected** — see notes for details

### 6.5 Cancelling a Booking

Bookings can be cancelled before payment via the **Cancel** button in My Bookings. Once payment is confirmed, contact the facility owner directly for any changes.

### 6.6 Booking Without an Account (Guest Checkout)

You don't need to register to book a court, join Open Play, or buy a bundle. On the booking form, if you're not logged in, fill in the **Your Info** card instead (Name, Email, Phone) and submit as normal.

- A confirmation email is sent to you with a **private link** to manage that booking — bookmark it or keep the email, since it's your only way back in.
- The link takes you straight to your Payment page, in whatever state it's in — pay, view "awaiting confirmation," or cancel (before the date, if not yet confirmed).
- If you'd rather track everything in one place, use the **Log in** link on the booking form instead to book with a registered account.
- Booking again later with the same email reuses your guest profile rather than creating a new one each time.

### 6.7 Joining Open Play

Some courts reserve certain hours for **Open Play** — a shared session you join as an individual player rather than booking the whole court.

1. On the court's availability grid, an Open Play hour shows the price per head and remaining spots instead of the usual "Book" state.
2. Click it, choose how many spots you need, and submit (with or without an account — see [6.6](#66-booking-without-an-account-guest-checkout)).
3. Pay the same way as a regular booking — manual GCash/Maya proof or instant card/e-wallet, depending on what the facility offers.
4. If the session fills up, the remaining-spots count reaches zero and it's no longer joinable.

### 6.8 Booking a Bundle

If a facility offers a **bundle** (e.g. two courts together at a flat price for weekday evenings), you'll be offered the bundle option when booking any of its member courts during that window. Confirming it books every member court together as one linked purchase — you'll see all of them together on your Payment and My Bookings pages.

---

## 7. Payments

### 7.1 How Payment Works

Two flows are supported, and a facility can offer either or both at once:

- **Manual** — CourtBook doesn't process the money itself. It tells customers where to send GCash/Maya payment, and gives the facility owner a way to verify the transfer against an uploaded screenshot.
- **Instant** — if the facility has connected a PayMongo account, customers pay by card or e-wallet through a secure checkout and the booking confirms automatically the moment payment succeeds — no manual review needed.

### 7.2 For Customers

After confirming a booking, you'll see the **Complete Payment** page.

**If instant payment is available**, you'll see a **Pay Instantly** card listing the accepted methods (e.g. GCash, Maya, Card, GrabPay, QRPh, Online Banking) — click **Pay Securely** and complete checkout. Your booking confirms right away.

**Otherwise, for manual payment**, you'll see:

- Booking summary (court, date, time, total)
- The facility's GCash and/or Maya number (with QR code, if provided)
- Payment instructions
- A form to submit your reference number and optional screenshot

**Steps:**

1. Open your GCash or Maya app.
2. Send the **exact amount** shown.
3. Copy the transaction/reference number.
4. Paste it into the form on the payment page.
5. (Optional) Upload your payment screenshot.
6. Click **Submit Payment Proof**.

### 7.3 For Facility Owners

When a manual payment is submitted, the booking appears in **Admin → Bookings** with status *Awaiting Verification*. Check your GCash/Maya inbox for the matching reference, open the submitted screenshot via **View Screenshot**, and click **Confirm Payment** or **Reject Payment** as appropriate. Instant (PayMongo) payments skip this step entirely — they confirm themselves.

---

## 8. Subscription — Free Trial and Pro

### 8.1 Free Trial

- New facility owners get a **30-day free trial** automatically.
- During the trial, **all core features** are available (unlimited courts, unlimited bookings, customer management).
- Custom branding (logo, custom site name) is **Pro-only** even during the trial.

### 8.2 Upgrading to Pro

Go to **Admin → Settings → Subscription → Upgrade to Pro** (or click the trial banner). Choose:

- **Monthly Plan** — ₱799/month
- **Annual Plan** — ₱7,588/year (save ~21%)

**Activation steps:**

1. Send payment to the CourtBook subscription GCash/Maya number shown on the Upgrade page.
2. Submit the reference number through the form.
3. CourtBook sales team verifies the payment (usually within 24 hours).
4. You receive an **Activation Key** by email.
5. Enter the key in **Admin → Settings → Subscription → Activate**.
6. Pro features unlock instantly.

### 8.3 What You Get with Pro

- Custom site name (your facility name replaces "CourtBook" everywhere)
- Custom logo in the navbar
- Custom tagline
- "PRO" badge on your facility page (builds customer trust)
- Priority support

---

## 9. Custom Branding (Pro)

Available once your subscription is **Active**.

In **Admin → Settings → Custom Branding** you can set:

- **Site Name** — replaces "CourtBook" in the navbar and emails
- **Tagline** — short line shown in the footer
- **Logo** — PNG/JPG/SVG image (recommended: transparent background, minimum 200px wide). Shown in the navbar instead of the text name.

Click **Save Settings** to apply. Changes are visible immediately to your customers.

---

## 10. Frequently Asked Questions

**Q: Can a customer book on more than one facility?**
A: Yes, but only one facility at a time is "preferred." Clicking a new `/sportshub/slug` link switches them.

**Q: Can I import existing bookings or customers?**
A: Not yet via the UI. Contact support for bulk-import help.

**Q: What if I forget my admin password?**
A: Use the **Forgot Password** link on the login page. A reset link will be sent to your registered email.

**Q: Can two customers book the same slot at the same time?**
A: No — the system enforces atomic availability. The first to submit wins; the second sees the slot greyed out and must choose another.

**Q: Will my data be lost when my trial ends?**
A: No. Your data is kept — but customer-facing features are restricted until you upgrade.

**Q: Can I delete a court permanently?**
A: It's safer to deactivate. Hard delete is only available if the court has no bookings.

**Q: Does CourtBook take a percentage of my bookings?**
A: No. All money goes directly from your customer to your GCash/Maya. CourtBook only charges the subscription fee.

**Q: Can I change my URL slug later?**
A: Yes, but old shared links will stop working. Make sure to re-share the new link.

**Q: Do customers need to create an account to book?**
A: No. Anyone can book a court, join Open Play, or buy a bundle as a guest — just fill in name, email, and phone on the booking form. They manage the booking afterward via a private link emailed to them instead of logging in. See [6.6 Booking Without an Account](#66-booking-without-an-account-guest-checkout).

**Q: What is Open Play, and how is it different from a normal booking?**
A: Open Play is an hour range you host and open to the public — individual players sign up for a spot (not the whole court) at a price per head, up to a max headcount you set. Configure it under a court's [Default Schedule & Rates](#default-schedule--rates), and review sign-ups (including payment screenshots) under **Admin → Courts → Open Play Sign-ups**.

**Q: Where do I see my Open Play sign-ups once they're confirmed?**
A: They now appear in **Admin → Bookings ("All Bookings")** alongside regular and bundle bookings, tagged with an "Open Play" badge — no need to check a separate page once they're no longer pending.

**Q: I ticked several payment methods in Settings, but customers still only see one. What happened?**
A: This was a bug where a facility's very first-ever Settings save could silently ignore ticked payment methods (falling back to QRPh only) — it's now fixed. If you're still only seeing one method, go to **Admin → Settings** and re-tick/save your desired methods; also double-check each ticked method is actually activated on your PayMongo dashboard, since PayMongo only offers methods there regardless of what's ticked in CourtBook.

**Q: Why do times now show like "6:00 PM" instead of "18:00"?**
A: All times across the app (booking grids, schedules, admin pages, emails) now display in 12-hour format with AM/PM for readability. Nothing about how bookings work has changed — just how the time is displayed.

---

## 11. Support

- **Email:** courtbooksolutions@gmail.com
- **Phone:** +63 917 675 0210
- **Hours:** Monday–Sunday, 9 AM – 6 PM (PHT)

For urgent issues during your free trial or active subscription, email us with your **Facility Name** and **Reference Number** for faster handling.

---

*CourtBook — Book your court anytime, anywhere.*
