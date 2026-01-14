import express, { type Express, type Request, type Response, type NextFunction } from "express";
import { createServer, type Server } from "http";
import path from "path";
import { insertPurchaseSchema, insertQrCodeSchema, insertFuelPackageSchema, insertStationSchema, insertFuelTypeSchema } from "../../shared/database/schema";
import { ZodError } from "zod";
import session from "express-session";
import { generateVerificationCode, sendVerificationCode } from "../../shared/infrastructure/twilio";
import { getUncachableStripeClient, getStripePublishableKey } from "../../shared/infrastructure/stripe";
import multer from "multer";

import { usersRepository } from "../../features/users/users.repository";
import { verificationRepository } from "../../features/users/verification.repository";
import { stationsRepository } from "../../features/stations/stations.repository";
import { fuelTypesRepository } from "../../features/stations/fuel-types.repository";
import { packagesRepository } from "../../features/stations/packages.repository";
import { qrCodesRepository } from "../../features/inventory/qr-codes.repository";
import { purchasesRepository } from "../../features/purchases/purchases.repository";
import { notificationsRepository } from "../../features/notifications/notifications.repository";
import { vouchersRepository } from "../../features/vouchers/vouchers.repository";
import vouchersRouter from "./routes/vouchers";
import paymentsRouter from "../../routes/payments";

export async function registerRoutes(
  httpServer: Server,
  app: Express
): Promise<Server> {
  // Configure session middleware
  app.use(session({
    secret: process.env.SESSION_SECRET || 'fuel-flow-secret-key-change-in-production',
    resave: false,
    saveUninitialized: false,
    cookie: {
      secure: process.env.NODE_ENV === 'production',
      httpOnly: true,
      maxAge: 24 * 60 * 60 * 1000 // 24 hours
    }
  }));

  // ... imports 

  // await setupAuth(app); // Removed Replit Auth

  // Serve uploaded files statically
  app.use('/uploads', express.static(path.join(process.cwd(), 'uploads')));

  app.get("/api/health", (req, res) => {
    res.json({ status: 'ok' });
  });

  // Replit-specific user endpoint removed
  /*
  app.get('/api/auth/user', isAuthenticated, async (req: any, res) => {
    try {
      const userId = req.user.claims.sub;
      const user = await usersRepository.getUser(userId);
      if (!user) {
        return res.status(404).json({ message: "User not found" });
      }
      res.json(user);
    } catch (error) {
      console.error("Error fetching user:", error);
      res.status(500).json({ message: "Failed to fetch user" });
    }
  });
    */

  // Serve uploads directory
  app.use('/uploads', express.static(path.join(process.cwd(), 'uploads')));
  app.use("/api/vouchers", vouchersRouter);
  app.use("/api/payments", paymentsRouter);

  const otpRequestTracker = new Map<string, { count: number; resetAt: number }>();

  const isRateLimited = (identifier: string, limit: number = 3, windowMs: number = 60000): boolean => {
    const now = Date.now();
    const entry = otpRequestTracker.get(identifier);

    if (!entry || entry.resetAt < now) {
      otpRequestTracker.set(identifier, { count: 1, resetAt: now + windowMs });
      return false;
    }

    if (entry.count >= limit) {
      return true;
    }

    entry.count++;
    return false;
  };

  app.post('/api/auth/phone/send-code', async (req, res) => {
    try {
      const clientIp = req.ip || req.socket.remoteAddress || 'unknown';

      if (isRateLimited(`send:${clientIp}`, 3, 60000)) {
        return res.status(429).json({ error: "Too many requests. Please wait a minute." });
      }

      const { phone } = req.body;

      if (!phone || typeof phone !== 'string') {
        return res.status(400).json({ error: "Phone number is required" });
      }

      const sanitizedPhone = phone.replace(/[\s-]/g, '');
      const phoneRegex = /^\+?[1-9]\d{6,14}$/;
      if (!phoneRegex.test(sanitizedPhone)) {
        return res.status(400).json({ error: "Invalid phone number format" });
      }

      const normalizedPhone = sanitizedPhone.startsWith('+') ? sanitizedPhone : `+${sanitizedPhone}`;
      const code = generateVerificationCode();

      // Log verification code for development
      console.log(`[DEV] Verification code for ${normalizedPhone}: ${code}`);

      const isSent = await sendVerificationCode(normalizedPhone, code);

      if (!isSent) {
        return res.status(500).json({ error: "Failed to send SMS" });
      }

      await verificationRepository.createPhoneVerification(normalizedPhone, code);

      res.json({ success: true, message: "Verification code sent" });
    } catch (error) {
      console.error("Error sending verification code:", error);
      res.status(500).json({ error: "Failed to send verification code" });
    }
  });

  const otpVerificationTracker = new Map<string, { count: number; resetAt: number }>();

  // Public Stations (for Map)
  app.get("/api/stations", async (_req, res) => {
    const allStations = await stationsRepository.getAllStations();
    res.json(allStations);
  });

  app.post('/api/auth/phone/verify', async (req, res) => {
    try {
      const clientIp = req.ip || req.socket.remoteAddress || 'unknown';

      if (isRateLimited(`verify:${clientIp}`, 5, 300000)) {
        return res.status(429).json({ error: "Too many attempts. Please wait 5 minutes." });
      }

      const { phone, code } = req.body;

      if (!phone || !code) {
        return res.status(400).json({ error: "Phone and code are required" });
      }

      if (typeof code !== 'string' || code.length !== 6 || !/^\d+$/.test(code)) {
        return res.status(400).json({ error: "Invalid code format" });
      }

      const normalizedPhone = phone.startsWith('+') ? phone : `+${phone}`;
      const phoneIdentifier = `phone:${normalizedPhone}`;
      const attemptEntry = otpVerificationTracker.get(phoneIdentifier);
      const now = Date.now();

      if (attemptEntry && attemptEntry.resetAt > now && attemptEntry.count >= 3) {
        return res.status(429).json({ error: "Too many failed attempts for this number" });
      }

      const verificationRecord = await verificationRepository.getLatestPhoneVerification(normalizedPhone);

      if (!verificationRecord) {
        return res.status(400).json({ error: "No verification pending or code expired" });
      }

      if (verificationRecord.code !== code) {
        if (!attemptEntry || attemptEntry.resetAt < now) {
          otpVerificationTracker.set(phoneIdentifier, { count: 1, resetAt: now + 300000 });
        } else {
          attemptEntry.count++;
        }
        return res.status(400).json({ error: "Invalid verification code" });
      }

      await verificationRepository.markPhoneVerified(verificationRecord.id);
      otpVerificationTracker.delete(phoneIdentifier);

      let user = await usersRepository.getUserByPhone(normalizedPhone);
      if (!user) {
        user = await usersRepository.createUserWithPhone(normalizedPhone);
      }

      req.session.regenerate((err) => {
        if (err) {
          console.error("Session regeneration error:", err);
          return res.status(500).json({ error: "Session error" });
        }

        (req.session as any).userId = user!.id;
        (req.session as any).phoneAuth = true;

        res.json({ success: true, user });
      });
    } catch (error) {
      console.error("Error verifying code:", error);
      res.status(500).json({ error: "Failed to verify code" });
    }
  });

  app.get('/api/auth/phone/user', async (req, res) => {
    try {
      const userId = (req.session as any)?.userId;
      const isPhoneAuth = (req.session as any)?.phoneAuth;

      if (!userId || !isPhoneAuth) {
        return res.status(401).json({ error: "Not authenticated" });
      }

      const user = await usersRepository.getUser(userId);
      if (!user) {
        return res.status(404).json({ error: "User not found" });
      }

      res.json(user);
    } catch (error) {
      console.error("Error fetching phone auth user:", error);
      res.status(500).json({ error: "Failed to fetch user" });
    }
  });

  app.post('/api/auth/phone/logout', async (req, res) => {
    try {
      req.session.destroy((err) => {
        if (err) {
          return res.status(500).json({ error: "Failed to logout" });
        }
        res.json({ success: true });
      });
    } catch (error) {
      console.error("Error logging out:", error);
      res.status(500).json({ error: "Failed to logout" });
    }
  });

  // DEV ONLY: Quick Login
  if (process.env.NODE_ENV !== "production") {
    app.post("/api/auth/dev-login", async (req, res) => {
      try {
        const email = "dev@example.com";
        let user = await usersRepository.getUserByEmail(email);

        if (!user) {
          user = await usersRepository.createUser({
            email,
            firstName: "Dev",
            lastName: "Tester",
          });
        }

        // Establish session
        req.session.regenerate((err) => {
          if (err) {
            console.error("Session error:", err);
            return res.status(500).json({ error: "Session creation failed" });
          }
          // Simulate Replit auth
          (req.session as any).userId = user!.id;
          // Also set phone auth to allow testing phone features if needed, 
          // but for now let's treat it as a standard user or maybe a hybrid.
          // Let's stick to standard user ID as primary.

          res.json({ success: true, user });
        });

      } catch (error) {
        console.error("Dev login error:", error);
        res.status(500).json({ error: "Dev login failed" });
      }
    });
  }

  const checkAuthorization = async (req: any, res: any, next: any) => {
    // DEV MODE: Auto-authenticate with mock user
    if (process.env.NODE_ENV !== 'production') {
      req.authUserId = 'dev-user-123';
      req.authType = 'dev';
      return next();
    }

    const userId = (req.session as any)?.userId;
    const isPhoneAuth = (req.session as any)?.phoneAuth;

    if (userId && isPhoneAuth) {
      req.authUserId = userId;
      req.authType = 'phone';
      return next();
    }

    if (req.user?.claims?.sub) {
      req.authUserId = req.user.claims.sub;
      req.authType = 'replit';
      return next();
    }

    return res.status(401).json({ error: "Authentication required" });
  };

  app.get("/api/packages", async (req, res) => {
    try {
      const packages = await packagesRepository.getAllPackages();
      res.json(packages);
    } catch (error) {
      console.error("Error fetching packages:", error);
      res.status(500).json({ error: "Failed to fetch packages" });
    }
  });

  app.get("/api/packages/station/:stationId", async (req, res) => {
    try {
      const { stationId } = req.params;
      const packages = await packagesRepository.getPackagesByStation(stationId);
      res.json(packages);
    } catch (error) {
      console.error("Error fetching packages by station:", error);
      res.status(500).json({ error: "Failed to fetch packages" });
    }
  });

  app.post("/api/purchases", checkAuthorization, async (req: any, res) => {
    try {
      const userId = req.authUserId;
      const purchaseData = insertPurchaseSchema.parse({
        ...req.body,
        sessionId: userId,
      });
      const purchase = await purchasesRepository.createPurchase(purchaseData);
      res.json(purchase);
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid purchase data", details: error.errors });
      } else {
        console.error("Error creating purchase:", error);
        res.status(500).json({ error: "Failed to create purchase" });
      }
    }
  });

  app.get("/api/purchases/my", checkAuthorization, async (req: any, res) => {
    try {
      const userId = req.authUserId;
      const purchaseHistory = await purchasesRepository.getPurchasesByUserId(userId);

      const purchaseRecords = await Promise.all(
        purchaseHistory.map(async (purchase) => {
          if ((purchase.qrCodeId || purchase.voucherId) && purchase.status === "delivered") {
            const data = await purchasesRepository.getPurchaseWithQrCode(purchase.id);
            return data || purchase;
          }
          return purchase;
        })
      );

      res.json(purchaseRecords);
    } catch (error) {
      console.error("Error fetching purchases:", error);
      res.status(500).json({ error: "Failed to fetch purchases" });
    }
  });

  app.get("/api/purchases/session/:sessionId", async (req, res) => {
    try {
      const { sessionId } = req.params;
      const purchaseHistory = await purchasesRepository.getPurchasesByUserId(sessionId);

      const purchaseRecords = await Promise.all(
        purchaseHistory.map(async (purchase) => {
          if ((purchase.qrCodeId || purchase.voucherId) && purchase.status === "delivered") {
            const data = await purchasesRepository.getPurchaseWithQrCode(purchase.id);
            return data || purchase;
          }
          return purchase;
        })
      );

      res.json(purchaseRecords);
    } catch (error) {
      console.error("Error fetching purchases:", error);
      res.status(500).json({ error: "Failed to fetch purchases" });
    }
  });

  app.post("/api/purchases/:id/complete", async (req, res) => {
    try {
      const { id } = req.params;
      const purchaseId = parseInt(id);

      const purchaseRecord = await purchasesRepository.getPurchase(purchaseId);
      if (!purchaseRecord) {
        return res.status(404).json({ error: "Purchase not found" });
      }

      // 1. Try finding a voucher (Primary Inventory)
      // 1. Try finding and consuming vouchers (Primary Inventory) - Transactional
      try {
        const assigned = await vouchersRepository.assignVouchersToPurchase(
          purchaseId,
          purchaseRecord.sessionId, // User ID
          purchaseRecord.stationName, // Provider
          purchaseRecord.fuelName,    // Fuel Type
          purchaseRecord.liters,      // Liters Amount
          1                           // Quantity (assuming 1 pack per purchase for now)
        );

        // Update purchase status
        // Link the first voucher if 1:1
        const voucherId = assigned.length > 0 ? assigned[0].id : undefined;
        // Cast voucherId to any if needed because updatePurchaseStatus expects string?
        // Purchase table voucher_id is UUID string.
        await purchasesRepository.updatePurchaseStatus(purchaseId, "delivered", undefined, voucherId);

      } catch (err: any) {
        console.warn(`Primary voucher assignment failed for Purchase ${purchaseId}: ${err.message}`);

        // 2. Fallback to legacy QR codes table
        const availableQr = await qrCodesRepository.getAvailableQrCode(
          purchaseRecord.stationId,
          purchaseRecord.fuelName,
          purchaseRecord.liters
        );

        if (!availableQr) {
          return res.status(404).json({ error: "No vouchers or QR codes available for this order." });
        }

        await qrCodesRepository.markQrCodeAsSold(availableQr.id, purchaseId);
        await purchasesRepository.updatePurchaseStatus(purchaseId, "delivered", availableQr.id);
      }

      const finalizedPurchase = await purchasesRepository.getPurchaseWithQrCode(purchaseId);
      res.json(finalizedPurchase);
    } catch (error) {
      console.error("Error completing purchase:", error);
      res.status(500).json({ error: "Failed to complete purchase" });
    }
  });

  app.get("/api/inventory", async (req, res) => {
    try {
      const inventory = await vouchersRepository.getInventoryAggregation();
      res.json(inventory);
    } catch (error: any) {
      console.error("Error fetching inventory:", error);
      res.status(500).json({ error: "Failed to fetch inventory", details: error.message, stack: error.stack });
    }
  });

  app.get("/api/vouchers/my", checkAuthorization, async (req: any, res) => {
    try {
      const userId = req.authUserId;
      // In dev mode, userId might be 'dev-user-123'
      const vouchers = await vouchersRepository.getUserVouchers(userId);
      res.json(vouchers);
    } catch (error) {
      console.error("Error fetching user vouchers:", error);
      res.status(500).json({ error: "Failed to fetch user vouchers" });
    }
  });

  app.post("/api/qr-codes", async (req, res) => {
    try {
      const payload = insertQrCodeSchema.parse(req.body);
      const record = await qrCodesRepository.createQrCode(payload);
      res.json(record);
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid QR code data", details: error.errors });
      } else {
        console.error("Error creating QR code:", error);
        res.status(500).json({ error: "Failed to create QR code" });
      }
    }
  });

  app.post("/api/qr-codes/bulk", async (req, res) => {
    try {
      const { qrCodes } = req.body;
      if (!Array.isArray(qrCodes)) {
        return res.status(400).json({ error: "qrCodes must be an array" });
      }

      const created = await Promise.all(
        qrCodes.map(async (qr) => {
          const payload = insertQrCodeSchema.parse(qr);
          return await qrCodesRepository.createQrCode(payload);
        })
      );

      res.json({ count: created.length, qrCodes: created });
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid QR code data", details: error.errors });
      } else {
        console.error("Error bulk creating QR codes:", error);
        res.status(500).json({ error: "Failed to bulk create QR codes" });
      }
    }
  });

  app.post("/api/payments/simulate", checkAuthorization, async (req: any, res) => {
    try {
      const { purchaseId, scenario } = req.body;
      const id = parseInt(purchaseId);
      const purchase = await purchasesRepository.getPurchase(id);
      if (!purchase) return res.status(404).json({ error: "Purchase not found" });

      if (scenario === 'failure') {
        await purchasesRepository.updatePurchaseStatus(id, "failed");
        return res.json({ status: "failed" });
      }

      // Success Scenario logic
      try {
        const assigned = await vouchersRepository.assignVouchersToPurchase(
          id,
          purchase.sessionId,
          purchase.stationName,
          purchase.fuelName, // Normalized name used in storage
          purchase.liters,
          1
        );

        const voucherId = assigned.length > 0 ? assigned[0].id : undefined;
        await purchasesRepository.updatePurchaseStatus(id, "delivered", undefined, voucherId);

        const finalized = await purchasesRepository.getPurchaseWithQrCode(id);
        return res.json({ status: "success", purchase: finalized });

      } catch (err: any) {
        console.warn(`Simulate payment business error: ${err.message}`);
        await purchasesRepository.updatePurchaseStatus(id, "failed");
        return res.status(409).json({ error: err.message, code: "BUSINESS_VIOLATION" });
      }

    } catch (error) {
      console.error("Simulation error:", error);
      res.status(500).json({ error: "Internal simulation failed" });
    }
  });

  app.post("/api/checkout", checkAuthorization, async (req: any, res) => {
    try {
      const { packageId, stationId, stationName, fuelType, fuelName, liters, price } = req.body;
      const userId = req.authUserId;

      const purchaseRecord = await purchasesRepository.createPurchase({
        sessionId: userId,
        packageId,
        stationId,
        stationName,
        fuelType,
        fuelName,
        liters,
        price,
        status: "pending",
      });

      // Return purchase ID so frontend can call simulate
      res.json({ purchaseId: purchaseRecord.id });

    } catch (error) {
      console.error("Error creating purchase:", error);
      res.status(500).json({ error: "Failed to create purchase" });
    }
  });


  app.get("/api/stripe/config", async (req, res) => {
    try {
      const publishableKey = await getStripePublishableKey();
      res.json({ publishableKey });
    } catch (error) {
      console.error("Error getting Stripe config:", error);
      res.status(500).json({ error: "Failed to get Stripe config" });
    }
  });

  app.get("/api/admin/qr-codes", async (req, res) => {
    try {
      const list = await qrCodesRepository.getAllQrCodes();
      res.json(list);
    } catch (error) {
      console.error("Error fetching QR codes:", error);
      res.status(500).json({ error: "Failed to fetch QR codes" });
    }
  });

  app.delete("/api/admin/qr-codes/:id", async (req, res) => {
    try {
      const id = parseInt(req.params.id);
      await qrCodesRepository.deleteQrCode(id);
      res.json({ success: true });
    } catch (error) {
      console.error("Error deleting QR code:", error);
      res.status(500).json({ error: "Failed to delete QR code" });
    }
  });

  app.get("/api/admin/purchases", async (req, res) => {
    try {
      const list = await purchasesRepository.getAllPurchases();
      res.json(list);
    } catch (error) {
      console.error("Error fetching purchases:", error);
      res.status(500).json({ error: "Failed to fetch purchases" });
    }
  });

  app.get("/api/admin/packages/suggestions", async (req, res) => {
    try {
      const suggestions = await packagesRepository.getPackageSuggestions();
      res.json(suggestions);
    } catch (error) {
      console.error("Error fetching package suggestions:", error);
      res.status(500).json({ error: "Failed to fetch suggestions" });
    }
  });

  app.get("/api/admin/packages", async (req, res) => {
    try {
      const list = await packagesRepository.getAllPackages();
      res.json(list);
    } catch (error) {
      console.error("Error fetching packages:", error);
      res.status(500).json({ error: "Failed to fetch packages" });
    }
  });

  app.post("/api/admin/packages", async (req, res) => {
    try {
      const payload = insertFuelPackageSchema.parse(req.body);
      const record = await packagesRepository.createPackage(payload);
      res.json(record);
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid package data", details: error.errors });
      } else {
        console.error("Error creating package:", error);
        res.status(500).json({ error: "Failed to create package" });
      }
    }
  });

  app.put("/api/admin/packages/:id", async (req, res) => {
    try {
      const record = await packagesRepository.updatePackage(req.params.id, req.body);
      res.json(record);
    } catch (error) {
      console.error("Error updating package:", error);
      res.status(500).json({ error: "Failed to update package" });
    }
  });

  app.delete("/api/admin/packages/:id", async (req, res) => {
    try {
      await packagesRepository.deletePackage(req.params.id);
      res.json({ success: true });
    } catch (error) {
      console.error("Error deleting package:", error);
      res.status(500).json({ error: "Failed to delete package" });
    }
  });

  app.get("/api/admin/stations", async (req, res) => {
    try {
      const list = await stationsRepository.getAllStations();
      res.json(list);
    } catch (error) {
      console.error("Error fetching stations:", error);
      res.status(500).json({ error: "Failed to fetch stations" });
    }
  });

  app.post("/api/admin/stations", async (req, res) => {
    try {
      const payload = insertStationSchema.parse(req.body);
      const record = await stationsRepository.createStation(payload);
      res.json(record);
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid station data", details: error.errors });
      } else {
        console.error("Error creating station:", error);
        res.status(500).json({ error: "Failed to create station" });
      }
    }
  });

  app.put("/api/admin/stations/:id", async (req, res) => {
    try {
      const record = await stationsRepository.updateStation(req.params.id, req.body);
      res.json(record);
    } catch (error) {
      console.error("Error updating station:", error);
      res.status(500).json({ error: "Failed to update station" });
    }
  });

  app.delete("/api/admin/stations/:id", async (req, res) => {
    try {
      await stationsRepository.deleteStation(req.params.id);
      res.json({ success: true });
    } catch (error) {
      console.error("Error deleting station:", error);
      res.status(500).json({ error: "Failed to delete station" });
    }
  });

  app.get("/api/admin/fuel-types", async (req, res) => {
    try {
      const list = await fuelTypesRepository.getAllFuelTypes();
      res.json(list);
    } catch (error) {
      console.error("Error fetching fuel types:", error);
      res.status(500).json({ error: "Failed to fetch fuel types" });
    }
  });

  app.post("/api/admin/fuel-types", async (req, res) => {
    try {
      const payload = insertFuelTypeSchema.parse(req.body);
      const record = await fuelTypesRepository.createFuelType(payload);
      res.json(record);
    } catch (error) {
      if (error instanceof ZodError) {
        res.status(400).json({ error: "Invalid fuel type data", details: error.errors });
      } else {
        console.error("Error creating fuel type:", error);
        res.status(500).json({ error: "Failed to create fuel type" });
      }
    }
  });

  app.put("/api/admin/fuel-types/:id", async (req, res) => {
    try {
      const record = await fuelTypesRepository.updateFuelType(req.params.id, req.body);
      res.json(record);
    } catch (error) {
      console.error("Error updating fuel type:", error);
      res.status(500).json({ error: "Failed to update fuel type" });
    }
  });

  app.delete("/api/admin/fuel-types/:id", async (req, res) => {
    try {
      await fuelTypesRepository.deleteFuelType(req.params.id);
      res.json({ success: true });
    } catch (error) {
      console.error("Error deleting fuel type:", error);
      res.status(500).json({ error: "Failed to delete fuel type" });
    }
  });

  app.get("/api/admin/users", async (req, res) => {
    try {
      const list = await usersRepository.getAllUsers();
      res.json(list);
    } catch (error) {
      console.error("Error fetching users:", error);
      res.status(500).json({ error: "Failed to fetch users" });
    }
  });

  app.post("/api/referral/create", checkAuthorization, async (req: any, res) => {
    try {
      const userId = req.authUserId;
      const { code } = req.body;

      const existing = await usersRepository.getUserByReferralCode(code);
      if (existing) {
        return res.status(400).json({ error: "Referral code already taken" });
      }

      const user = await usersRepository.updateUser(userId, { referralCode: code });
      res.json(user);
    } catch (error) {
      console.error("Error creating referral code:", error);
      res.status(500).json({ error: "Failed to create referral code" });
    }
  });

  app.post("/api/referral/redeem", checkAuthorization, async (req: any, res) => {
    try {
      const userId = req.authUserId;
      const { code } = req.body;

      // 1. Validate code
      const referrer = await usersRepository.getUserByReferralCode(code);
      if (!referrer) {
        return res.status(404).json({ error: "Invalid referral code" });
      }

      // 2. Validate user isn't redeeming their own code
      if (referrer.id === userId) {
        return res.status(400).json({ error: "Cannot redeem your own code" });
      }

      // 3. Check if user was already referred
      const currentUser = await usersRepository.getUser(userId);
      if (currentUser?.referredBy) {
        return res.status(400).json({ error: "You have already redeemed a referral code" });
      }

      // 4. Apply referral
      await usersRepository.updateUser(userId, { referredBy: referrer.id });

      // 5. Credit bonus to Referrer (e.g. 50 UAH)
      const referrerBonus = (referrer.bonusBalance || 0) + 50;
      await usersRepository.updateUser(referrer.id, { bonusBalance: referrerBonus });

      // 6. Credit bonus to Referee (e.g. 20 UAH)
      const userBonus = (currentUser?.bonusBalance || 0) + 20;
      await usersRepository.updateUser(userId, { bonusBalance: userBonus });

      // 7. Notify Referrer
      await notificationsRepository.createNotification({
        userId: referrer.id,
        title: "New Referral Reward!",
        message: `User ${currentUser?.phone || 'someone'} used your code! You got 50 UAH bonus.`,
      });

      res.json({ success: true, message: "Referral code redeemed" });

    } catch (error) {
      console.error("Error redeeming referral code:", error);
      res.status(500).json({ error: "Failed to redeem code" });
    }
  });

  return httpServer;
}
