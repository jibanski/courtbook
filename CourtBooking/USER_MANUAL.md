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

---

## 1. Overview

CourtBook is a multi-tenant online booking platform for sports facilities (badminton, tennis, basketball, pickleball, futsal, volleyball, swimming, billiards, table tennis, football). Each facility owner gets their own private workspace, a shareable booking link, and a customer-facing courts page.

**Key concepts:**

- **Facility** — a sports venue owned by one admin account. Each facility has its own courts, time slots, payment details, and customer base.
- **Court** — a single bookable resource (e.g. *Badminton Court 1*) belonging to a facility.
- **Time slot** — a specific bookable window on a specific date (e.g. *June 5, 8:00–9:00 AM on Court 1*).
- **Booking** — a customer's reservation of a time slot, awaiting payment and admin confirmation.
- **Shareable URL** — your unique `/f/your-slug` link that customers visit to view and book your courts.

---

## 2. Account Types

| Role | Who | Sees |
|---|---|---|
| **Admin** (Facility Owner) | The person managing a sports facility | Their own admin dashboard, only their courts and bookings |
| **Customer** | Players who want to book courts | The facility page they were invited to, their own bookings |

A customer can only belong to **one preferred facility** at a time. The facility is locked in the first time they visit a shared link, register, or log in from that link.

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
5. **Add courts** — Admin → Courts → *Add Court*. Set name, sport, hours, price per hour, indoor/outdoor.
6. **Generate time slots** — Admin → Courts → *Manage Slots* on each court to create bookable windows.
7. **Share your link** — Admin → Settings → copy the **Shareable Booking URL** and send it to your customers.

That's it. Customers can now visit your link, register, and book.

---

## 4. Quick Start for Customers

1. **Click the link** your facility owner sent you (e.g. `https://courtbook-solutions.up.railway.app/f/greenfield-sports`).
2. **Browse courts** — pick the sport you want and tap a court card.
3. **Pick a date and time slot**.
4. **Sign up or log in** when prompted.
5. **Confirm the booking** and you'll be sent to the **Payment** page.
6. **Send payment** via GCash or Maya to the number shown.
7. **Submit your proof** — enter the reference number (and optionally upload a screenshot).
8. **Wait for confirmation** — the facility admin will verify your payment, usually within a few hours.
9. **Check My Bookings** to see the status.

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
- **URL Slug** — the unique part of your shareable link (`/f/your-slug`). Lowercase letters, numbers, and hyphens only.
  > ⚠ Changing your slug will break any links you've already shared.

#### Payment Methods

- **GCash Number / Name** — the number customers will send money to.
- **Maya Number / Name** — alternative payment method.
- **Payment Instructions** — free-form text shown on the payment page (e.g. *"Send the exact amount and include your booking ID in the notes."*).

You can fill in either GCash, Maya, or both. If neither is filled in, customers see a warning to contact you directly.

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
- **Opening Hour / Closing Hour** — 24-hour format (e.g. 6 to 22 means 6 AM to 10 PM)
- **Price Per Hour** — in PHP

#### Editing or deactivating a court

Use **Edit** to change details, or **Toggle Active** to hide a court without deleting it. Deactivated courts are hidden from customers but historical bookings are preserved.

#### Managing time slots

Click **Manage Slots** on a court. You can:

- **Generate slots** for a date range (defines bookable windows of N hours each)
- **Delete a slot** to remove that window from availability
- **Toggle a slot** active/inactive

If you don't generate any time slots, the system falls back to hourly slots between the court's opening and closing hours.

### 5.4 Managing Bookings

**Admin → Bookings** lists every reservation made on your courts.

For each booking you can:

- **View payment proof** — see the screenshot and reference number the customer submitted
- **Confirm Payment** — marks the booking as paid and confirmed
- **Reject Payment** — sends the booking back to *Pending* with a note for the customer
- **Update Status** — Pending → Confirmed → Completed, or Cancel

> 🔒 You can only see and act on bookings for courts you own. Other facilities' bookings are not visible.

### 5.5 Payment Verification Workflow

1. Customer submits a booking → status `Pending Payment`.
2. Customer pays via GCash/Maya → submits reference + screenshot → status `Awaiting Verification`.
3. **You** check your GCash/Maya app for the matching reference number.
4. If valid → click **Confirm Payment**. Booking is now `Confirmed`.
5. If invalid or duplicate → click **Reject Payment**. Customer is notified and can resubmit.

---

## 6. Customer Guide

### 6.1 Finding Your Facility

The first time you click a `/f/your-slug` link, the facility is remembered for 7 days via a cookie. Once you register or log in, the facility becomes **permanently associated** with your account — every subsequent login takes you straight to that facility's courts.

If you ever need to switch to a different facility, simply click a new `/f/different-slug` link.

### 6.2 Browsing Courts

The facility page shows all active courts. You can filter by sport using the chips at the top. Each court card shows:

- Court name and sport
- Indoor/Outdoor badge
- Opening hours
- Price per hour

Click **Book** to open the court's availability calendar.

### 6.3 Booking a Court

1. **Pick a date** from the date picker (up to 30 days ahead).
2. **Pick an available slot** — green = available, red = already booked.
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

---

## 7. Payments

### 7.1 How Payment Works

CourtBook does **not** process payments itself. Instead it tells customers where to send their money via GCash or Maya, and gives the facility owner a way to verify the transfer.

### 7.2 For Customers

After confirming a booking, you'll see the **Complete Payment** page with:

- Booking summary (court, date, time, total)
- The facility's GCash and/or Maya number
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

When a payment is submitted, the booking appears in **Admin → Bookings** with status *Awaiting Verification*. Check your GCash/Maya inbox for the matching reference and click **Confirm Payment** or **Reject Payment** as appropriate.

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
A: Yes, but only one facility at a time is "preferred." Clicking a new `/f/slug` link switches them.

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

---

## 11. Support

- **Email:** sales@courtbook.com
- **Phone:** +63 917 675 0210
- **Hours:** Monday–Saturday, 9 AM – 6 PM (PHT)

For urgent issues during your free trial or active subscription, email us with your **Facility Name** and **Reference Number** for faster handling.

---

*CourtBook — Book your court anytime, anywhere.*
