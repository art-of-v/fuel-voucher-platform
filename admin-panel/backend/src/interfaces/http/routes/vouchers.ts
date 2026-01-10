import { Router } from "express";
import multer from "multer";
import { storage } from "../../../infrastructure/storage";
import { uploadFile } from "../../../infrastructure/services/file_storage";
import { ImportOrchestrator } from "../../../infrastructure/services/import_orchestrator";

const router = Router();
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 50 * 1024 * 1024 } });

// 1. ARCHITECTURAL CHANGE: Delegate to Orchestrator
router.post("/import", upload.array("files"), async (req, res) => {
    console.log("Import endpoint hit (V2 Orchestrator)");
    try {
        const files = req.files as Express.Multer.File[];
        if (!files || files.length === 0) return res.status(400).json({ error: "No files" });

        // Create Job Record
        const job = await storage.createImportJob({
            adminId: "admin",
            totalFiles: files.length,
            status: "processing",
            source: "strict_orchestrator_import"
        } as any);

        console.log(`Job Created: ${job.id}`);

        // Fire and Forget (Handled by Worker)
        ImportOrchestrator.getInstance().queueJob(job.id, files);

        res.json({ jobId: job.id, message: "Import Job Queued Successfully (V2 Pipeline)" });

    } catch (e) {
        console.error("Endpoint Failed", e);
        res.status(500).json({ error: "Upload Failed", details: e instanceof Error ? e.message : String(e) });
    }
});

router.get("/import-status/:id", async (req, res) => {
    const job = await storage.getImportJob(req.params.id);
    if (!job) return res.status(404).json({ error: "Job not found" });
    res.json(job);
});

router.get("/", async (req, res) => {
    const { status, provider, fuelType, page = 1, limit = 50, sortBy, sortDirection } = req.query;
    const result = await storage.getVouchers({
        status: status as string,
        provider: provider as string,
        fuelType: fuelType as string,
        limit: Number(limit),
        offset: (Number(page) - 1) * Number(limit),
        sortBy: sortBy as string,
        sortDirection: sortDirection as 'asc' | 'desc'
    });
    res.json(result);
});

// IMPORTANT: Specific routes must come BEFORE generic /:id routes
router.patch("/:id/mark-used", async (req, res) => {
    try {
        const voucherId = req.params.id;
        console.log(`[MARK-USED] Attempting to mark voucher as used: ${voucherId}`);

        const voucher = await storage.getVoucherById(voucherId);
        console.log(`[MARK-USED] Voucher found:`, voucher ? `Yes (status: ${voucher.status})` : 'No');

        if (!voucher) {
            console.log(`[MARK-USED] Voucher not found in database: ${voucherId}`);
            return res.status(404).json({ message: "Voucher not found" });
        }

        await storage.updateVoucher(voucherId, { status: "used" });
        console.log(`[MARK-USED] Successfully marked voucher as used: ${voucherId}`);
        res.json({ message: "Voucher marked as used", status: "used" });
    } catch (error) {
        console.error("Error marking voucher as used:", error);
        res.status(500).json({ error: "Failed to mark voucher as used" });
    }
});

router.patch("/:id/restore", async (req, res) => {
    try {
        const voucherId = req.params.id;
        const voucher = await storage.getVoucherById(voucherId);

        if (!voucher) {
            return res.status(404).json({ message: "Voucher not found" });
        }

        await storage.updateVoucher(voucherId, { status: "available" });
        res.json({ message: "Voucher restored", status: "available" });
    } catch (error) {
        console.error("Error restoring voucher:", error);
        res.status(500).json({ error: "Failed to restore voucher" });
    }
});

router.patch("/:id", async (req, res) => {
    try {
        const voucher = await storage.updateVoucher(req.params.id, req.body);
        res.json(voucher);
    } catch (error) {
        res.status(500).json({ error: "Update failed" });
    }
});

router.post("/bulk-action", async (req, res) => {
    try {
        const { action, ids, targetUserId } = req.body;

        if (action === "delete_all") {
            await storage.deleteAllVouchers();
            return res.json({ success: true });
        }

        if (!Array.isArray(ids) || ids.length === 0) return res.status(400).json({ error: "No IDs provided" });
        const updates: any = {};
        if (action === "activate") updates.status = "available";
        if (action === "expire") updates.status = "expired";
        if (action === "assign") {
            if (!targetUserId) return res.status(400).json({ error: "Target User ID required" });
            updates.status = "assigned"; updates.assignedToUserId = targetUserId;
        }
        if (action === "delete") {
            await Promise.all(ids.map(id => storage.deleteVoucher(id)));
            return res.json({ success: true, count: ids.length });
        }
        await Promise.all(ids.map(id => storage.updateVoucher(id, updates)));
        res.json({ success: true, count: ids.length });
    } catch (error) {
        res.status(500).json({ error: "Bulk action failed" });
    }
});

router.get("/available", async (req, res) => {
    const vouchers = await storage.getAvailableVouchers();
    res.json(vouchers);
});

router.get("/my", async (req, res) => {
    try {
        // Get user ID from session (same pattern as other endpoints)
        const userId = (req.session as any)?.userId || "dev-user-123";
        const vouchers = await storage.getUserVouchers(userId);
        res.json(vouchers);
    } catch (error) {
        console.error("Error fetching user vouchers:", error);
        res.status(500).json({ error: "Failed to fetch vouchers" });
    }
});

export default router;
