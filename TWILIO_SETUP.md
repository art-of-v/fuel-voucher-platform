# Twilio SMS Authentication - Setup Guide

## ✅ Current Status
Your Fuel Flow app **already has Twilio integration built-in**! I've configured most of it for you.

---

## 📋 Final Setup Steps

### **Step 1: Get Your Twilio Phone Number**

1. Go to: https://console.twilio.com/us1/develop/phone-numbers/manage/incoming
2. You should see your free trial phone number (e.g., `+1 234 567 8900`)
3. **Copy this number** - you'll need it in Step 2

---

### **Step 2: Update Environment Variables**

Open `c:\Projects\Fuel-Flow\.env` and update line 24:

```bash
TWILIO_PHONE_NUMBER=+12345678900  # Replace with YOUR Twilio number from Step 1
```

**Your current credentials (already configured):**
```bash
TWILIO_ACCOUNT_SID=AC083a0fdc637b86afdc659a6c5bd69ef2
TWILIO_AUTH_TOKEN=***REDACTED***
TWILIO_PHONE_NUMBER=your_twilio_phone_number_here  # ← UPDATE THIS
```

---

### **Step 3: Add Verified Phone Numbers (Trial Account Limitation)**

⚠️ **Important**: Twilio trial accounts can only send SMS to **verified numbers**.

1. Go to: https://console.twilio.com/us1/develop/phone-numbers/manage/verified
2. Click **"Add a new number"**
3. Enter your Ukrainian phone number in E.164 format: `+380XXXXXXXXX`
4. Twilio will send you a verification code
5. Enter the code to verify

**Repeat this for any phone numbers you want to test with.**

---

### **Step 4: Rebuild Docker Containers**

The backend needs to restart to pick up the new environment variables:

```bash
docker-compose down
docker-compose up --build -d
```

Or just restart the backend:

```bash
docker-compose restart admin-backend
```

---

## 🧪 Testing the Integration

### **1. Test from Mobile App**

1. Open the Fuel Flow app
2. Go to the phone authentication screen
3. Enter your verified phone number (e.g., `+380XXXXXXXXX`)
4. Click "НАДІСЛАТИ КОД" (Send Code)
5. You should receive an SMS with a 6-digit code
6. Enter the code and verify

### **2. Test from API (Optional)**

**Send Code:**
```bash
curl -X POST http://localhost:4000/api/auth/phone/send-code \
  -H "Content-Type: application/json" \
  -d '{"phone": "+380XXXXXXXXX"}'
```

**Verify Code:**
```bash
curl -X POST http://localhost:4000/api/auth/phone/verify \
  -H "Content-Type: application/json" \
  -d '{"phone": "+380XXXXXXXXX", "code": "123456"}'
```

---

## 📱 How It Works

### **Flow:**

1. **User enters phone number** → App sends to `/api/auth/phone/send-code`
2. **Backend generates 6-digit code** → Stores in database
3. **Twilio sends SMS** → User receives code on their phone
4. **User enters code** → App sends to `/api/auth/phone/verify`
5. **Backend verifies code** → Creates user session
6. **User is authenticated** → Can make purchases

### **Security Features:**

- ✅ **Rate Limiting**: Max 3 SMS requests per minute per IP
- ✅ **Code Expiration**: Codes expire after 5 minutes
- ✅ **Attempt Limiting**: Max 3 verification attempts per phone number
- ✅ **Session Management**: Secure session cookies

---

## 🆓 Twilio Trial Limitations

Your free trial account has:

- ✅ **$15.50 USD credit** (enough for ~500 SMS)
- ⚠️ **Can only send to verified numbers**
- ⚠️ **SMS includes "Sent from your Twilio trial account" prefix
- ✅ **All features available** (just with trial branding)

### **To Remove Limitations:**

1. Upgrade to a paid account (no monthly fee, pay-as-you-go)
2. Add $20 to your account
3. All trial restrictions removed

---

## 🔧 Troubleshooting

### **"Failed to send SMS"**

**Possible causes:**
1. Phone number not verified in Twilio console
2. Invalid phone number format (must be E.164: `+380XXXXXXXXX`)
3. Twilio credentials not set correctly

**Check backend logs:**
```bash
docker logs fuel-admin-backend
```

### **"Invalid verification code"**

- Code expires after 5 minutes
- Max 3 attempts per phone number
- Code is case-sensitive (all digits)

### **SMS not received**

1. Check if phone number is verified in Twilio
2. Check Twilio logs: https://console.twilio.com/us1/monitor/logs/sms
3. Ensure phone number is in E.164 format

---

## 📝 API Endpoints

Your app has these phone auth endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/phone/send-code` | POST | Send verification code |
| `/api/auth/phone/verify` | POST | Verify code and login |
| `/api/auth/phone/user` | GET | Get current user |
| `/api/auth/phone/logout` | POST | Logout user |

---

## ✅ What's Already Done

- ✅ Twilio SDK installed
- ✅ Environment variables configured in Docker
- ✅ Twilio service created
- ✅ Phone auth API routes implemented
- ✅ Rate limiting implemented
- ✅ Session management configured
- ✅ Random code generation enabled
- ✅ Database integration for verification codes

---

## 🎯 Next Steps

1. **Get your Twilio phone number** from the console
2. **Update `.env`** with the phone number
3. **Verify your test phone numbers** in Twilio
4. **Restart Docker** containers
5. **Test the flow** in your mobile app

---

## 💡 Tips

- **Use E.164 format**: Always include country code (e.g., `+380` for Ukraine)
- **Test with your own number first**: Verify it works before sharing
- **Monitor Twilio console**: Check SMS logs if issues occur
- **Keep trial credits**: Each SMS costs ~$0.0075, so you have plenty for testing

---

**Your Twilio integration is ready! Just add your phone number and restart Docker.** 🚀
