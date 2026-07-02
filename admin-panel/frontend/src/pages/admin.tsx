import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2, Loader2, FileUp, Filter, CheckSquare, ChevronUp, ChevronDown, ArrowUpDown, ChevronLeft, ChevronRight, FileSignature, Edit2, Package, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/api-client";
import { Layout } from "@/components/layout";
import { useI18n } from "@/lib/i18n";
import { QRCodeCanvas } from "qrcode.react";
import QRCode from "qrcode";
// import { useToast } from "@/hooks/use-toast";

export default function AdminScreen() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState('stations');
  const [newStation, setNewStation] = useState({ id: "", name: "", color: "#00ff80", logoText: "" });
  const [editingStation, setEditingStation] = useState<any>(null);
  const [newFuelType, setNewFuelType] = useState({ id: "", name: "", stationId: "", basePrice: 0, discountPrice: 0 });
  const [editingFuelType, setEditingFuelType] = useState<any>(null);
  const [newQr, setNewQr] = useState({ stationId: "", fuelType: "", qrCodeUrl: "", liters: 10 });
  const [newPackage, setNewPackage] = useState({ id: "", stationId: "", fuelTypeId: "", fuelName: "", liters: 10, price: 0, originalPrice: 0 });

  // Import state
  const [importFiles, setImportFiles] = useState<File[]>([]);
  const [isImporting, setIsImporting] = useState(false);
  const [importStatus, setImportStatus] = useState<'idle' | 'processing' | 'completed' | 'error'>('idle');
  const [importResult, setImportResult] = useState({ success: 0, errors: 0, existing: 0, modelUsed: '' });
  const [importProgress, setImportProgress] = useState({ processed: 0, total: 0 });
  const [selectedQrId, setSelectedQrId] = useState<string | null>(null);

  // Fetch single voucher details when modal is open to get decrypted QR
  const { data: fullVoucherData, isLoading: isVoucherLoading } = useQuery({
    queryKey: ["/api/admin/vouchers", selectedQrId],
    queryFn: async () => {
      if (!selectedQrId) return null;
      const res = await apiRequest<any, any>("GET", `/api/admin/vouchers/${selectedQrId}`);
      return res;
    },
    enabled: !!selectedQrId
  });

  // Derived state to keep logic working (if something relied on selectedQrData string, we can mock it or remove usage)
  const selectedQrData = selectedQrId; // Simply truthy to show modal

  const [isDragging, setIsDragging] = useState(false);

  // Enhanced Voucher State
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("desc");
  const [filterFuelType, setFilterFuelType] = useState("");
  const [filterStatus, setFilterStatus] = useState("");
  const [filterProvider, setFilterProvider] = useState("");
  const [filterAmount, setFilterAmount] = useState("");
  const [filterExpirationDate, setFilterExpirationDate] = useState("");
  const [selectedVoucherIds, setSelectedVoucherIds] = useState<Set<string>>(new Set());
  const [page, setPage] = useState(1);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [selectedSignature, setSelectedSignature] = useState<string | null>(null);
  const limit = 50;

  interface StationType {
    id: string;
    name: string;
    logoText: string;
    color: string;
  }

  interface FuelTypeType {
    id: string;
    name: string;
    stationId: string;
    basePrice: number;
    discountPrice: number;
  }

  interface QrCodeType {
    id: number;
    stationId: string;
    fuelType: string;
    liters: number;
    qrCodeUrl: string;
    status: "available" | "sold";
  }

  interface PurchaseType {
    id: string;
    userId: string;
    stationName: string;
    fuelName: string;
    liters: number;
    price: number;
    status: "pending" | "pending_qr" | "completed" | "delivered" | "failed";
    qrCodeUrl?: string;
    createdAt: string;
  }

  interface PackageType {
    id: string;
    stationId: string;
    fuelTypeId: string;
    fuelName: string;
    liters: number;
    price: number;
    originalPrice: number;
  }

  interface ContractType {
    id: string;
    title: string;
    content: string;
    version: string;
    status: string;
    createdAt: string;
  }

  interface UserContractType {
    id: string;
    userName: string;
    companyName: string;
    contractTitle: string;
    signedAt: string;
    signatureData: string;
  }

  interface UserType {
    id: string;
    email: string | null;
    phone: string | null;
    firstName: string | null;
    lastName: string | null;
    birthdate: string | null;
    profileImageUrl: string | null;
    referralCode: string | null;
    referredBy: string | null;
    bonusBalance: number;
    createdAt: string;
  }

  const { data: stationsList = [] } = useQuery<StationType[]>({
    queryKey: ["/api/admin/stations"],
  });

  const { data: usersList = [] } = useQuery<UserType[]>({
    queryKey: ["/api/admin/users"],
  });

  const { data: fuelTypesList = [] } = useQuery<FuelTypeType[]>({
    queryKey: ["/api/admin/fuel-types"],
  });

  const { data: qrCodes = [] } = useQuery<QrCodeType[]>({
    queryKey: ["/api/admin/qr-codes"],
  });

  const { data: purchases = [] } = useQuery<PurchaseType[]>({
    queryKey: ["/api/admin/purchases"],
  });

  const { data: vouchersResponse } = useQuery<any>({
    queryKey: ["/api/admin/vouchers", page, sortBy, sortOrder, filterFuelType, filterStatus, filterProvider, filterAmount, filterExpirationDate],
    queryFn: async () => {
      const params = new URLSearchParams({
        page: page.toString(),
        limit: limit.toString(),
        sortBy,
        sortDirection: sortOrder,
        ...(filterFuelType ? { fuelType: filterFuelType } : {}),
        ...(filterStatus ? { status: filterStatus } : {}),
        ...(filterProvider ? { provider: filterProvider } : {}),
        ...(filterAmount ? { amount: filterAmount } : {}),
        ...(filterExpirationDate ? { expirationDate: filterExpirationDate } : {})
      });
      const res = await apiRequest<any, any>("GET", `/api/admin/vouchers?${params.toString()}`);
      return res;
    }
  });

  const vouchers = vouchersResponse?.data || [];
  const totalVouchers = vouchersResponse?.total || 0;
  const globalTotal = vouchersResponse?.globalTotal || 0;
  const dropdownFuelTypes = vouchersResponse?.fuelTypes || [];
  const dropdownProviders = vouchersResponse?.providers || [];
  const dropdownStatuses = vouchersResponse?.statuses || [];
  const dropdownAmounts = vouchersResponse?.amounts || [];

  interface SuggestionType {
    suggestedId: string;
    stationId: string;
    stationName: string;
    fuelTypeId: string;
    fuelName: string;
    liters: number;
    price: number;
    originalPrice: number;
  }

  const { data: packages = [] } = useQuery<PackageType[]>({
    queryKey: ["/api/admin/packages"],
  });

  const { data: suggestions = [] } = useQuery<SuggestionType[]>({
    queryKey: ["/api/admin/packages/suggestions"],
    enabled: activeTab === 'packages'
  });

  const { data: contractsList = [] } = useQuery<ContractType[]>({
    queryKey: ["/api/admin/legal-entity/contracts"],
    enabled: activeTab === 'contracts'
  });

  const { data: signedContractsList = [] } = useQuery<UserContractType[]>({
    queryKey: ["/api/admin/legal-entity/signed-contracts"],
    enabled: activeTab === 'contracts'
  });

  const [suggestionPrices, setSuggestionPrices] = useState<Record<string, { price: number | ""; originalPrice: number | "" }>>({});

  const handleSuggestionPriceChange = (id: string, field: 'price' | 'originalPrice', value: string) => {
    setSuggestionPrices((prev: Record<string, { price: number | ""; originalPrice: number | "" }>) => ({
      ...prev,
      [id]: {
        ...prev[id],
        [field]: value === "" ? "" : parseInt(value) || 0
      }
    }));
  };

  const createFromSuggestion = (suggestion: SuggestionType) => {
    const prices = suggestionPrices[suggestion.suggestedId];
    if (!prices || !prices.price) return; // Validation: Price is required

    createPackageMutation.mutate({
      // Ensure unique ID prefix if needed, or use suggestedId as is. SuggestedId is already unique-ish.
      id: suggestion.suggestedId,
      stationId: suggestion.stationId,
      fuelTypeId: suggestion.fuelTypeId,
      fuelName: suggestion.fuelName,
      liters: suggestion.liters,
      price: Number(prices.price),
      originalPrice: Number(prices.originalPrice || 0)
    });
  };

  const createStationMutation = useMutation({
    mutationFn: async (data: typeof newStation) => {
      const res = await apiRequest<any, any>("POST", "/api/admin/stations", data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
      setNewStation({ id: "", name: "", color: "#00ff80", logoText: "" });
    },
  });

  const deleteStationMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest<any, any>("DELETE", `/api/admin/stations/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
    },
  });

  const createFuelTypeMutation = useMutation({
    mutationFn: async (data: typeof newFuelType) => {
      const res = await apiRequest<any, any>("POST", "/api/admin/fuel-types", data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
      setNewFuelType({ id: "", name: "", stationId: "", basePrice: 0, discountPrice: 0 });
    },
  });

  const deleteFuelTypeMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest<any, any>("DELETE", `/api/admin/fuel-types/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
    },
  });

  const updateStationMutation = useMutation({
    mutationFn: async (data: StationType) => {
      const res = await apiRequest<any, any>("PUT", `/api/admin/stations/${data.id}`, data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
      setEditingStation(null);
    },
  });

  const updateFuelTypeMutation = useMutation({
    mutationFn: async (data: FuelTypeType) => {
      const res = await apiRequest<any, any>("PUT", `/api/admin/fuel-types/${data.id}`, data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
      setEditingFuelType(null);
    },
  });

  const createQrMutation = useMutation({
    mutationFn: async (data: typeof newQr) => {
      const res = await apiRequest<any, any>("POST", "/api/admin/qr-codes", data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/qr-codes"] });
      setNewQr({ ...newQr, qrCodeUrl: "" });
    },
  });

  const deleteQrMutation = useMutation({
    mutationFn: async (id: number) => {
      await apiRequest<any, any>("DELETE", `/api/admin/qr-codes/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/qr-codes"] });
    },
  });

  const createPackageMutation = useMutation({
    mutationFn: async (data: typeof newPackage) => {
      const res = await apiRequest<any, any>("POST", "/api/admin/packages", data);
      return res;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/packages"] });
      setNewPackage({ id: "", stationId: "", fuelTypeId: "", fuelName: "", liters: 10, price: 0, originalPrice: 0 });
    },
  });

  const deletePackageMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest<any, any>("DELETE", `/api/admin/packages/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/packages"] });
    },
  });



  const bulkDeleteMutation = useMutation({
    mutationFn: async () => {
      await apiRequest<any, any>("POST", "/api/admin/vouchers/bulk-action", { action: "delete_all", ids: [] });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
    },
  });

  const deleteSelectedMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest<any, any>("POST", "/api/admin/vouchers/bulk-action", { action: "delete", ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
      setSelectedVoucherIds(new Set());
    },
  });

  const toggleSort = (column: string) => {
    if (sortBy === column) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc");
    } else {
      setSortBy(column);
      setSortOrder("desc");
    }
  };

  // Helper component for 1:1 QR code matching
  // Priority: 1) Stored image from PDF (pixel-perfect) 2) QR regeneration (fallback)
  const VoucherQRCode = ({ value, size, provider, imageUrl }: { value: string; size: number; provider?: string; imageUrl?: string | null }) => {
    const [dataUrl, setDataUrl] = useState<string | null>(null);
    const isWog = provider?.toLowerCase().includes("wog");

    useEffect(() => {
      // If we have a stored image from PDF, use it directly (pixel-perfect)
      if (imageUrl) {
        setDataUrl(null); // Not needed, imageUrl takes priority
        return;
      }

      if (isWog && value) {
        // WOG fallback: Byte mode, ECC L, Mask 5, Version 4 + Trailing Newline
        // This is a 1:1 match for WOG's 33x33 matrix (Version 4)
        const finalString = value.endsWith('\n') ? value : value + '\n';

        try {
          const canvas = document.createElement('canvas');
          const ctx = canvas.getContext('2d');
          canvas.width = 33 * 4;
          canvas.height = 33 * 4;

          if (ctx) {
            ctx.fillStyle = '#000000';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.font = 'bold 12px monospace';
            ctx.fillStyle = '#FFFFFF';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';

            const lines = finalString.split('\n');
            const lineHeight = 4;
            const totalHeight = lines.length * lineHeight;
            const startY = (canvas.height - totalHeight) / 2 + lineHeight / 2;

            for (let i = 0; i < lines.length; i++) {
              ctx.fillText(lines[i], canvas.width / 2, startY + i * lineHeight);
            }

            const dataUrl = canvas.toDataURL('image/png');
            setDataUrl(dataUrl);
          }
        } catch (error) {
          console.error('Failed to generate QR canvas:', error);
        }
        return;
      }

      // Default QR code generation
      try {
        const img = new Image();
        img.onload = () => {
          const canvas = document.createElement('canvas');
          const ctx = canvas.getContext('2d');
          canvas.width = size;
          canvas.height = size;

          if (ctx) {
            ctx.fillStyle = '#FFFFFF';
            ctx.fillRect(0, 0, size, size);

            if (dataUrl) {
              ctx.drawImage(img, 0, 0, size, size);
            }
          }
        };
        img.src = `data:image/png;base64,${QRCode.toDataURL(value)}`;
      } catch (error) {
        console.error('Failed to generate QR code:', error);
      }
    }, [value, size, provider, imageUrl]);

    if (imageUrl) {
      return <img src={imageUrl} alt="Voucher QR Code" style={{ width: size, height: size, objectFit: 'contain' }} />;
    }

    if (dataUrl) {
      return <img src={dataUrl} alt="Voucher QR Code" style={{ width: size, height: size, objectFit: 'contain' }} />;
    }

    return <div style={{ width: size, height: size, backgroundColor: '#f0f0f0', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>Loading QR...</div>;
  };

  // Additional components would continue here...

  return (
    <Layout activeTab={activeTab} setActiveTab={setActiveTab}>
      <div className="p-6">
        {/* Admin Dashboard Content */}
        <h1 className="text-2xl font-bold mb-6">Admin Dashboard</h1>

        {/* Stations Tab */}
        {activeTab === 'stations' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Stations</h2>
            {/* Station management UI would go here */}
          </div>
        )}

        {/* Fuel Types Tab */}
        {activeTab === 'fueltypes' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Fuel Types</h2>
            {/* Fuel type management UI would go here */}
          </div>
        )}

        {/* Packages Tab */}
        {activeTab === 'packages' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Fuel Packages</h2>
            {/* Package management UI would go here */}
          </div>
        )}

        {/* Purchases Tab */}
        {activeTab === 'purchases' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Purchases</h2>
            {/* Purchase management UI would go here */}
          </div>
        )}

        {/* Vouchers Tab */}
        {activeTab === 'vouchers' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Vouchers</h2>
            {/* Voucher management UI would go here */}
          </div>
        )}

        {/* Users Tab */}
        {activeTab === 'users' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Users</h2>
            {/* User management UI would go here */}
          </div>
        )}

        {/* QR Codes Tab */}
        {activeTab === 'qrcodes' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">QR Codes</h2>
            {/* QR code management UI would go here */}
          </div>
        )}

        {/* Import Tab */}
        {activeTab === 'vouchers' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Import Vouchers</h2>
            {/* Voucher import UI would go here */}
          </div>
        )}

        {/* Contracts Tab */}
        {activeTab === 'contracts' && (
          <div>
            <h2 className="text-xl font-semibold mb-4">Legal Entity Contracts</h2>
            {/* Contract management UI would go here */}
          </div>
        )}
      </div>
    </Layout>
  );
}
