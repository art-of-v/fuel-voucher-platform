import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Trash2, Loader2, FileUp, Filter, CheckSquare, ChevronUp, ChevronDown, ArrowUpDown, ChevronLeft, ChevronRight, FileSignature, Package, X, ArrowLeft, CheckCircle, XCircle, QrCode, BarChart, Building, ScrollText, Bug } from "lucide-react";
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
import { toast } from "sonner";
import { Layout } from "@/components/layout";
import { useI18n } from "@/lib/i18n";
import { isLoggedIn, sendCode, verifyCode, clearTokens, fetchCurrentUser, refreshAccessToken, type CurrentUser } from "@/lib/admin-auth";
import ProvidersTab from "@/components/ProvidersTab";
import AuditTab from "@/components/AuditTab";
import ErrorLogsTab from "@/components/ErrorLogsTab";
import SettingsTab from "@/components/SettingsTab";
import DateInput from "@/components/DateInput";
import { formatDate, formatDateTime } from "@/lib/utils";

function orderStatusKey(status: string): string {
  const camel = status.charAt(0).toLowerCase() + status.slice(1);
  if (camel === 'pendingpayment' || camel === 'pendingfulfillment') return 'pending';
  return camel;
}

function voucherStatusKey(status: string): string {
  return status.charAt(0).toLowerCase() + status.slice(1);
}

export default function AdminScreen() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [loggedIn, setLoggedIn] = useState(isLoggedIn());
  const [checkingAuth, setCheckingAuth] = useState(true);
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loginPhone, setLoginPhone] = useState("");
  const [loginCode, setLoginCode] = useState("");
  const [loginStep, setLoginStep] = useState<"phone" | "code">("phone");
  const [loginLoading, setLoginLoading] = useState(false);
  const [loginError, setLoginError] = useState("");

  const handleSendCode = async () => {
    setLoginLoading(true);
    setLoginError("");
    try {
      await sendCode(loginPhone);
      setLoginStep("code");
    } catch (e: any) {
      setLoginError(e.message || "Failed to send code");
    }
    setLoginLoading(false);
  };

  const handleVerifyCode = async () => {
    setLoginLoading(true);
    setLoginError("");
    try {
      await verifyCode(loginPhone, loginCode);
      const u = await fetchCurrentUser();
      setUser(u);
      setLoggedIn(true);
    } catch (e: any) {
      setLoginError(e.message || "Failed to verify code");
    }
    setLoginLoading(false);
  };

  useEffect(() => {
    if (loggedIn && !user) {
      fetchCurrentUser()
        .then(setUser)
        .catch(async () => {
          const refreshed = await refreshAccessToken();
          if (refreshed) {
            try { const u = await fetchCurrentUser(); setUser(u); } catch { clearTokens(); setLoggedIn(false); }
    } else {
          }
        });
    }
  }, [loggedIn, user]);

  useEffect(() => {
    refreshAccessToken().then(refreshed => {
      if (refreshed) {
        fetchCurrentUser().then(setUser).catch(() => {});
        setLoggedIn(true);
      }
      setCheckingAuth(false);
    });
  }, []);

  const [activeTab, setActiveTab] = useState(() => {
    return localStorage.getItem('admin_active_tab') || 'stations';
  });

  const handleTabChange = (tab: string) => {
    setActiveTab(tab);
    localStorage.setItem('admin_active_tab', tab);
  };
  // Report state
  const [reportUserId, setReportUserId] = useState("");
  const [reportFromDate, setReportFromDate] = useState("");
  const [reportToDate, setReportToDate] = useState("");
  const [reportTrigger, setReportTrigger] = useState(0);
  const [showReconciliationAct, setShowReconciliationAct] = useState(false);

  const toDateInput = (d: Date) => {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  };

  const formatDisplayDate = (iso: string) => {
    if (!iso) return '';
    const parts = iso.split('-');
    if (parts.length !== 3) return iso;
    return `${parts[2]}.${parts[1]}.${parts[0]}`;
  };

  const setReportQuickDate = (preset: 'yesterday' | 'week' | 'month' | '30days') => {
    const now = new Date();
    if (preset === 'yesterday') {
      now.setDate(now.getDate() - 1);
      const s = toDateInput(now);
      setReportFromDate(s);
      setReportToDate(s);
    } else if (preset === 'week') {
      const monday = new Date(now);
      monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
      setReportFromDate(toDateInput(monday));
      setReportToDate(toDateInput(now));
    } else if (preset === '30days') {
      const past = new Date(now);
      past.setDate(now.getDate() - 30);
      setReportFromDate(toDateInput(past));
      setReportToDate(toDateInput(now));
    } else {
      const first = new Date(now.getFullYear(), now.getMonth(), 1);
      setReportFromDate(toDateInput(first));
      setReportToDate(toDateInput(now));
    }
  };



  // Import state
  const [importFiles, setImportFiles] = useState<File[]>([]);
  const [isImporting, setIsImporting] = useState(false);
  const [importStatus, setImportStatus] = useState<'idle' | 'processing' | 'completed' | 'error'>('idle');
  const [importResult, setImportResult] = useState({ success: 0, errors: 0, existing: 0, modelUsed: '' });
  const [importProgress, setImportProgress] = useState({ processed: 0, total: 0 });
  const [importErrorMsg, setImportErrorMsg] = useState('');
  const [selectedQrId, setSelectedQrId] = useState<string | null>(null);

  // Fetch single voucher details when modal is open to get decrypted QR
  const { data: fullVoucherData, isLoading: isVoucherLoading } = useQuery({
    queryKey: ["/api/admin/vouchers", selectedQrId],
    queryFn: async () => {
      if (!selectedQrId) return null;
      const res = await apiRequest<any, any>("GET", `/api/admin/vouchers/${selectedQrId}`);
      return res;
    },
    enabled: !!user && !!selectedQrId
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
  const [showDeleteAllConfirm, setShowDeleteAllConfirm] = useState(false);
  const [refundTarget, setRefundTarget] = useState<PurchaseType | null>(null);
  const [selectedSignature, setSelectedSignature] = useState<string | null>(null);
  const [selectedImportId, setSelectedImportId] = useState<string | null>(null);
  const limit = 50;

  interface VoucherType {
    id: string;
    qrPayload: string;
    status: string;
    liters: number;
    fuelType: { name: string } | null;
    fuelTypeId: string;
    provider: string;
    expirationDate: string;
    voucherNumber: string;
    createdAtUtc: string;
  }

  interface PurchaseType {
    id: string;
    userId: string;
    provider: string;
    fuelTypeId: string;
    liters: number;
    quantity: number;
    price: number;
    status: string;
    monobankInvoiceId: string | null;
    monobankStatus: string | null;
    createdAtUtc: string;
    fulfilledAtUtc: string | null;
    voucherCount: number;
    refundableAmountKopecks: number;
    refundStatus: string | null;
    refundMonobankStatus: string | null;
    lineItems: {
      id: string;
      provider: string;
      fuelTypeId: string;
      liters: number;
      quantity: number;
      unitPrice: number;
      lineTotal: number;
    }[];
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
    isDeleted: boolean;
    createdAt: string;
  }

  const { data: usersList = [] } = useQuery<UserType[]>({
    queryKey: ["/api/admin/users"],
    enabled: !!user,
    queryFn: async () => {
      return await apiRequest<any, UserType[]>("GET", "/api/admin/users");
    }
  });

  const deleteUserMutation = useMutation({
    mutationFn: async (userId: string) => {
      await apiRequest<any, unknown>("DELETE", `/api/admin/users/${userId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/users"] });
      toast.success(t('users.deleteSuccess'));
    },
    onError: (err: Error) => {
      toast.error(`${t('users.deleteFailed')}: ${err.message}`);
    },
  });

  const { data: purchases = [] } = useQuery<PurchaseType[]>({
    queryKey: ["/api/admin/purchases"],
    enabled: !!user,
  });

  const refundPurchaseMutation = useMutation({
    mutationFn: async (orderId: string) => {
      return await apiRequest<any, { success: boolean; refundId: string; amountKopecks: number; status: string }>(
        "POST",
        `/api/admin/orders/${orderId}/refund`,
        {}
      );
    },
    onSuccess: (data) => {
      setRefundTarget(null);
      queryClient.invalidateQueries({ queryKey: ["/api/admin/purchases"] });
      toast.success(
        data.success
          ? t('purchases.refundSuccess', (data.amountKopecks / 100).toFixed(2))
          : t('purchases.refundFailed')
      );
    },
    onError: (err: Error) => {
      setRefundTarget(null);
      toast.error(`${t('purchases.refundFailed')}: ${err.message}`);
    },
  });

  const { data: vouchersResponse, isLoading: isVouchersLoading } = useQuery<{
    data: VoucherType[];
    total: number;
    globalTotal: number;
    fuelTypes: string[];
    providers: string[];
    statuses: string[];
    amounts: number[];
  }>({
    queryKey: ["/api/admin/vouchers", page, sortBy, sortOrder, filterFuelType, filterStatus, filterProvider, filterAmount, filterExpirationDate],
    enabled: !!user,
    staleTime: 30_000,
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

  const { data: contractsList = [] } = useQuery<ContractType[]>({
    queryKey: ["/api/admin/legal-entity/contracts"],
    enabled: !!user && activeTab === 'contracts'
  });

  const { data: signedContractsList = [] } = useQuery<UserContractType[]>({
    queryKey: ["/api/admin/legal-entity/signed-contracts"],
    enabled: !!user && activeTab === 'contracts'
  });

  const { data: importsList = [] } = useQuery<any[]>({
    queryKey: ["/api/admin/voucher-imports"],
    enabled: !!user && activeTab === 'imports',
    staleTime: 30_000,
  });

  const { data: importVouchers = [] } = useQuery<any[]>({
    queryKey: ["/api/admin/voucher-imports", selectedImportId, "vouchers"],
    staleTime: 30_000,
    queryFn: async () => {
      if (!selectedImportId) return [];
      const res = await apiRequest<any, any>("GET", `/api/admin/voucher-imports/${selectedImportId}/vouchers`);
      return res;
    },
    enabled: !!user && activeTab === 'imports' && !!selectedImportId
  });

  const { data: reconciliationData, isLoading: isReconLoading } = useQuery<any>({
    queryKey: ["/api/admin/reconciliation"],
    enabled: !!user && activeTab === 'reconciliation',
    staleTime: 30_000,
  });

  const {
    data: reportData,
    isLoading: isReportLoading
  } = useQuery<any>({
    queryKey: ["/api/admin/report", reportUserId, reportFromDate, reportToDate, reportTrigger],
    enabled: !!user && activeTab === 'reports' && reportTrigger > 0,
    queryFn: async () => {
      const params = new URLSearchParams();
      if (reportUserId) params.set('userId', reportUserId);
      if (reportFromDate) params.set('fromDate', new Date(reportFromDate).toISOString());
      if (reportToDate) params.set('toDate', new Date(reportToDate + 'T23:59:59').toISOString());
      const qs = params.toString();
      const res = await apiRequest<any, any>("GET", `/api/admin/report${qs ? '?' + qs : ''}`);
      return res;
    }
  });


  const bulkDeleteMutation = useMutation({
    mutationFn: async () => {
      await apiRequest("POST", "/api/admin/vouchers/bulk-action", { action: "delete_all", ids: [] });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteSelectedMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest("POST", "/api/admin/vouchers/bulk-action", { action: "delete", ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
      setSelectedVoucherIds(new Set());
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const activateVoucherMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest("POST", "/api/admin/vouchers/bulk-action", { action: "activate", ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-imports"] });
      if (selectedImportId) {
        queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-imports", selectedImportId, "vouchers"] });
      }
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const expireVoucherMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest("POST", "/api/admin/vouchers/bulk-action", { action: "expire", ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-imports"] });
      if (selectedImportId) {
        queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-imports", selectedImportId, "vouchers"] });
      }
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const toggleSort = (column: string) => {
    if (sortBy === column) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc");
    } else {
      setSortBy(column);
      setSortOrder("desc");
    }
  };




  const toggleSelectAll = () => {
    if (selectedVoucherIds.size === vouchers.length && vouchers.length > 0) {
      setSelectedVoucherIds(new Set());
    } else {
      const newSet = new Set(selectedVoucherIds);
      vouchers.forEach((v: VoucherType) => newSet.add(v.id));
      setSelectedVoucherIds(newSet);
    }
  };

  const toggleSelectRow = (id: string) => {
    const newSet = new Set(selectedVoucherIds);
    if (newSet.has(id)) newSet.delete(id);
    else newSet.add(id);
    setSelectedVoucherIds(newSet);
  };

  if (checkingAuth) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
        <Loader2 className="w-8 h-8 animate-spin text-primary" />
      </div>
    );
  }

  if (!loggedIn) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-8 w-full max-w-sm">
          <h1 className="text-2xl font-bold text-white mb-6 text-center">Admin Login</h1>

          {loginStep === "phone" ? (
            <>
              <Input
                placeholder="+380XXXXXXXXX"
                value={loginPhone}
                onChange={(e) => setLoginPhone(e.target.value)}
                className="mb-4"
              />
              <Button
                onClick={handleSendCode}
                disabled={loginLoading || !loginPhone}
                className="w-full"
              >
                {loginLoading ? <Loader2 className="animate-spin" /> : "Send Code"}
              </Button>
            </>
          ) : (
            <>
              <p className="text-gray-400 text-sm mb-4 text-center">
                Code sent to {loginPhone}
              </p>
              <Input
                placeholder="000000"
                value={loginCode}
                onChange={(e) => setLoginCode(e.target.value)}
                className="mb-4"
              />
              <Button
                onClick={handleVerifyCode}
                disabled={loginLoading || !loginCode}
                className="w-full"
              >
                {loginLoading ? <Loader2 className="animate-spin" /> : "Verify Code"}
              </Button>
            </>
          )}

          {loginError && (
            <p className="text-red-400 text-sm mt-4 text-center">{loginError}</p>
          )}
        </div>
      </div>
    );
  }

  return (
    <Layout activeTab={activeTab} onTabChange={handleTabChange} onLogout={() => { clearTokens(); setUser(null); setLoggedIn(false); }} user={user}>
      <div className="space-y-6">
        {/* Providers Tab (new consolidated view) */}
        {activeTab === 'providers' && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <Building className="w-6 h-6 text-primary" />
              {t('nav.providers')}
            </h2>
            <ProvidersTab />
          </div>
        )}

        {/* Users Tab */}
        {activeTab === 'users' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.name')}</th>
                    <th className="text-left p-4">{t('table.phone')}</th>
                    <th className="text-left p-4">{t('table.email')}</th>
                    <th className="text-left p-4">{t('table.birthdate')}</th>
                    <th className="text-left p-4">{t('table.bonusBalance')}</th>
                    <th className="text-left p-4">{t('table.referralCode')}</th>
                    <th className="text-left p-4">{t('table.referredBy')}</th>
                    <th className="text-left p-4">{t('table.status')}</th>
                    <th className="text-left p-4">{t('users.createdAt')}</th>
                    <th className="text-right p-4"></th>
                  </tr>
                </thead>
                <tbody>
                  {usersList.map((user) => (
                    <tr key={user.id} className="border-t border-gray-800 hover:bg-gray-800/30 transition-colors">
                      <td className="p-4 font-mono text-xs text-gray-400">{user.id}</td>
                      <td className="p-4 font-bold text-white">
                        {user.firstName || user.lastName ? `${user.firstName || ''} ${user.lastName || ''}`.trim() : <span className="text-gray-500 italic">No Name</span>}
                      </td>
                      <td className="p-4">{user.phone || <span className="text-gray-500 italic">N/A</span>}</td>
                      <td className="p-4">{user.email || <span className="text-gray-500 italic">N/A</span>}</td>
                      <td className="p-4">{user.birthdate ? formatDate(user.birthdate) : <span className="text-gray-500 italic">N/A</span>}</td>
                      <td className="p-4 text-primary font-bold">{user.bonusBalance || 0} UAH</td>
                      <td className="p-4 font-mono text-gray-300">{user.referralCode || <span className="text-gray-500 italic">N/A</span>}</td>
                      <td className="p-4 font-mono text-xs text-gray-400">{user.referredBy || '-'}</td>
                      <td className="p-4">
                        {user.isDeleted ? (
                          <span className="inline-flex items-center px-2 py-1 rounded bg-red-500/10 text-red-400 text-xs font-semibold">
                            {t('users.deleted')}
                          </span>
                        ) : (
                          <span className="inline-flex items-center px-2 py-1 rounded bg-emerald-500/10 text-emerald-400 text-xs font-semibold">
                            {t('users.active')}
                          </span>
                        )}
                      </td>
                      <td className="p-4 text-gray-400">
                        {formatDate(user.createdAt)}
                      </td>
                      <td className="p-4 text-right">
                        {!user.isDeleted && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              if (window.confirm(t('users.deleteConfirm'))) {
                                deleteUserMutation.mutate(user.id);
                              }
                            }}
                            disabled={deleteUserMutation.isPending}
                            className="text-red-400 hover:text-red-300 hover:bg-red-500/10"
                          >
                            <Trash2 className="w-4 h-4" />
                            <span className="ml-1 text-xs">{t('users.delete')}</span>
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                  {usersList.length === 0 && (
                    <tr>
                      <td colSpan={10} className="p-8 text-center text-gray-500">
                        No users found
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Purchases Tab */}
        {activeTab === 'purchases' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-x-auto">
              <table className="w-full">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.station')}</th>
                    <th className="text-left p-4">{t('table.fuel')}</th>
                    <th className="text-left p-4">{t('table.liters')}</th>
                    <th className="text-left p-4">{t('table.price')}</th>
                    <th className="text-left p-4">{t('common.status')}</th>
                    <th className="text-left p-4">{t('common.date')}</th>
                    <th className="text-left p-4">{t('table.vouchers')}</th>
                    <th className="text-left p-4">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {purchases.map((purchase) => (
                    <tr key={purchase.id} className="border-t border-gray-800">
                      <td className="p-4 font-mono text-xs">{purchase.id.slice(0, 8)}...</td>
                      <td className="p-4">{purchase.provider}</td>
                      <td className="p-4">{purchase.fuelTypeId}</td>
                      <td className="p-4">{purchase.liters}L</td>
                      <td className="p-4 text-primary font-bold">{purchase.price} UAH</td>
                      <td className="p-4">
                        <span className={`px-2 py-1 rounded text-xs ${purchase.status === "Fulfilled" ? "bg-green-500/20 text-green-400" :
                          purchase.status === "PartiallyFulfilled" ? "bg-yellow-500/20 text-yellow-400" :
                            purchase.status === "PartiallyRefunded" ? "bg-blue-500/20 text-blue-400" :
                              purchase.status === "Refunded" ? "bg-blue-500/20 text-blue-400" :
                                purchase.status === "PendingPayment" || purchase.status === "PendingFulfillment" ? "bg-orange-500/20 text-orange-400" :
                                  "bg-red-500/20 text-red-400"
                          }`}>
                          {t('order.status.' + orderStatusKey(purchase.status))}
                        </span>
                        {purchase.refundStatus && (
                          <span className={`block mt-1 px-2 py-1 rounded text-xs ${
                            purchase.refundStatus === "Completed" ? "bg-emerald-500/20 text-emerald-400" :
                              purchase.refundStatus === "Failed" ? "bg-red-500/20 text-red-400" :
                                "bg-gray-500/20 text-gray-400"
                          }`}>
                            {t('purchases.refundStatus.' + purchase.refundStatus)}
                          </span>
                        )}
                      </td>
                      <td className="p-4 text-gray-400 text-sm">
                        {formatDate(purchase.createdAtUtc)}
                      </td>
                      <td className="p-4">{purchase.voucherCount} / {purchase.quantity}</td>
                      <td className="p-4">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setRefundTarget(purchase)}
                          disabled={refundPurchaseMutation.isPending}
                          className="text-yellow-400 hover:text-yellow-300 hover:bg-yellow-500/10"
                        >
                          <XCircle className="w-4 h-4" />
                          <span className="ml-1 text-xs">{t('purchases.refund')}</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {purchases.length === 0 && (
                    <tr>
                      <td colSpan={9} className="p-8 text-center text-gray-500">
                        No purchases found
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Import Tab */}
        {activeTab === 'vouchers' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-8 mb-8">
              <div
                className={`flex flex-col items-center justify-center border-2 border-dashed rounded-lg p-12 transition-colors ${isDragging ? 'border-green-500 bg-green-500/10' : 'border-gray-700 hover:bg-gray-800/50'}`}
                onDragOver={(e) => { e.preventDefault(); setIsDragging(true); }}
                onDragLeave={() => setIsDragging(false)}
                onDrop={(e) => {
                  e.preventDefault();
                  setIsDragging(false);
                  if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                    setImportFiles(Array.from(e.dataTransfer.files));
                  }
                }}
              >
                <FileUp className="w-12 h-12 text-gray-500 mb-4" />
                <h3 className="text-xl font-bold mb-2">{t('import.title')}</h3>
                <p className="text-gray-400 mb-6 text-center max-w-md">{t('import.description')}</p>
                {importFiles.length > 0 && (
                  <div className="mb-4 text-center">
                    <p className="text-sm text-gray-400">{t('import.filesSelected', importFiles.length.toString())}</p>
                    <ul className="text-sm font-mono text-primary mt-1">
                      {importFiles.map((f, i) => <li key={i}>{f.name} ({Math.round(f.size / 1024)}KB)</li>)}
                    </ul>
                  </div>
                )}
                <div className="flex gap-4">
                  <Input
                    type="file"
                    multiple
                    className="hidden"
                    id="file-upload"
                    onChange={(e) => {
                      if (e.target.files && e.target.files.length > 0) {
                        setImportFiles(Array.from(e.target.files));
                        e.target.value = ""; // Allow re-selecting the same file
                      }
                    }}
                  />
                  <Button variant="outline" onClick={() => document.getElementById('file-upload')?.click()}>{t('import.clickSelect')}</Button>
                  <Button
                    onClick={async () => {
                      if (importFiles.length === 0) return;
                      setIsImporting(true);
                      setImportStatus('processing');
                      setImportErrorMsg('');
                      setImportResult({ success: 0, errors: 0, existing: 0, modelUsed: '' }); // Reset stats
                      const formData = new FormData();
                      importFiles.forEach(file => formData.append('file', file));
                      try {
                        const result = await apiRequest<any, { imported: number; failed: number; duplicates: number }>("POST", "/api/voucher-catalog/import", formData, undefined, 300_000, 0);

                        setImportProgress({ processed: 1, total: 1 });
                        setImportStatus(result.failed > 0 ? 'error' : 'completed');
                        setImportResult({
                          success: result.imported || 0,
                          errors: result.failed || 0,
                          existing: result.duplicates || 0,
                          modelUsed: ''
                        });

                      } catch (e) {
                        console.error('Import failed:', e, {
                          url: '/api/voucher-catalog/import',
                          method: 'POST',
                          timeoutMs: 300_000,
                          retries: 0,
                          files: importFiles.map(f => ({ name: f.name, size: f.size, type: f.type })),
                        });
                        setImportStatus('error');
                        setImportErrorMsg(e instanceof Error ? e.message : String(e));
                        // Don't overwrite if we already set partial results above
                        setImportResult(prev => {
                          if (prev.success > 0 || prev.existing > 0) return prev;
                          return { success: 0, errors: importFiles.length, existing: 0, modelUsed: '' };
                        });
                      }
                      setIsImporting(false);
                      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
                      setImportFiles([]);
                    }}
                    disabled={isImporting || importFiles.length === 0}
                    className="bg-green-600 text-white hover:bg-green-700"
                  >
                    {isImporting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <FileUp className="w-4 h-4 mr-2" />}
                    {t('import.start')}
                  </Button>
                </div>
                {importFiles.length > 0 && <div className="mt-4 text-green-400">{t('import.filesSelected', importFiles.length.toString())}</div>}
              </div>
            </div>

            {importStatus !== 'idle' && (
              <div className={`border rounded-xl p-4 mb-6 ${importStatus === 'error' ? 'bg-red-900/10 border-red-900/30' : 'bg-gray-900 border-gray-800'}`}>
                <div className="flex justify-between text-sm mb-2">
                  <span className="text-gray-400 font-bold">{t('common.status')}:
                    <span className={
                      importStatus === 'completed' ? "text-green-400 uppercase ml-2" :
                        importStatus === 'error' ? "text-red-400 uppercase ml-2" :
                          "text-blue-400 animate-pulse uppercase ml-2"
                    }>
                      {importStatus === 'completed' ? t('import.completed').toUpperCase() : importStatus === 'error' ? t('import.failed').toUpperCase() : t('import.processing').replace('...', '').toUpperCase()}
                    </span>
                  </span>
                  <span className="text-gray-400">
                    {importStatus === 'error' ? t('import.errorOccurred') : `${importProgress.processed} / ${importProgress.total} ${t('import.processed').toLowerCase()}`}
                  </span>
                  {(importStatus === 'completed' || importStatus === 'error') && (
                    <button onClick={() => setImportStatus('idle')} className="text-xs text-gray-500 hover:text-white underline ml-2">{t('import.close')}</button>
                  )}
                </div>
                {importResult.modelUsed && (
                  <div className="text-xs text-gray-500 mb-2">
                    {t('import.model')}: <span className="text-blue-400 font-mono">{importResult.modelUsed}</span>
                  </div>
                )}
                <div className="w-full bg-gray-800 rounded-full h-2.5 mb-2 overflow-hidden">
                  <div
                    className={`h-2.5 rounded-full transition-all duration-500 ${importStatus === 'completed' ? 'bg-green-500' :
                      importStatus === 'error' ? 'bg-red-500' :
                        'bg-blue-500 animate-pulse'
                      }`}
                    style={{ width: `${importProgress.total > 0 ? (importProgress.processed / importProgress.total) * 100 : 0}%` }}
                  ></div>
                </div>
                <div className="flex flex-col gap-2">
                  {importStatus === 'error' && (
                    <div className="flex flex-col gap-1">
                      <div className="text-xs text-red-400">{t('import.errorOccurred')}</div>
                      {importErrorMsg && <div className="text-xs text-red-300/90 font-mono break-all">{importErrorMsg}</div>}
                    </div>
                  )}
                  {importStatus === 'processing' && (
                    <div className="text-xs text-gray-500">{t('import.largeFileNote')}</div>
                  )}
                  <div className="flex justify-between text-xs text-gray-500 pt-1 border-t border-gray-800/50">
                    <span className="text-green-500">{t('import.successful')}: {importResult.success}</span>
                    <span className="text-red-500">{t('import.failedCount')}: {importResult.errors}</span>
                    <span className="text-orange-500">{t('import.duplicates')}: {importResult.existing}</span>
                  </div>
                </div>
              </div>
            )}

            {/* Controls Header */}
            <div className="flex flex-col gap-4 mb-4">
              <div className="flex flex-wrap justify-between items-center bg-gray-900 border border-gray-800 rounded-xl p-4 gap-4">
                <div className="flex gap-6 items-center flex-wrap">
                  <div>
                    <div className="text-xs text-gray-500 uppercase tracking-wider">{t('vouchers.total')}</div>
                    <div className="text-2xl font-bold text-white">{globalTotal}</div>
                  </div>
                  {filterFuelType && (
                    <div className="animate-in fade-in">
                      <div className="text-xs text-gray-500 uppercase tracking-wider">{t('vouchers.filtered')} ({filterFuelType})</div>
                      <div className="text-2xl font-bold text-primary">{totalVouchers}</div>
                    </div>
                  )}
                  {selectedVoucherIds.size > 0 && (
                    <div className="animate-in fade-in">
                      <div className="text-xs text-gray-500 uppercase tracking-wider">{t('vouchers.selected')}</div>
                      <div className="text-2xl font-bold text-blue-400">{selectedVoucherIds.size}</div>
                    </div>
                  )}
                </div>

                <div className="flex gap-2 items-center">
                  <div className="flex items-center gap-2">
                    <Select value={filterFuelType || "all"} onValueChange={(val) => { setFilterFuelType(val === "all" ? "" : val); setPage(1); }}>
                      <SelectTrigger className="w-[180px] bg-gray-800 border-gray-700 text-white rounded-lg h-9">
                        <div className="flex items-center gap-2">
                          <Filter className="w-3.5 h-3.5 text-gray-400" />
                          <SelectValue placeholder={t('vouchers.fuelType')} />
                        </div>
                      </SelectTrigger>
                      <SelectContent className="bg-gray-900 border-gray-800 text-white shadow-2xl">
                        <SelectItem value="all">{t('vouchers.allFuelTypes')}</SelectItem>
                        {dropdownFuelTypes.sort().map((name: string) => (
                          <SelectItem key={name} value={name}>{name}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>

                    <Select value={filterStatus || "all"} onValueChange={(val) => { setFilterStatus(val === "all" ? "" : val); setPage(1); }}>
                      <SelectTrigger className="w-[140px] bg-gray-800 border-gray-700 text-white rounded-lg h-9">
                        <SelectValue placeholder={t('vouchers.status')} />
                      </SelectTrigger>
                      <SelectContent className="bg-gray-900 border-gray-800 text-white shadow-2xl">
                        <SelectItem value="all">{t('vouchers.allStatuses')}</SelectItem>
                        {dropdownStatuses.map((s: string) => (
                          <SelectItem key={s} value={s}>{t('status.' + voucherStatusKey(s))}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>

                    <Select value={filterProvider || "all"} onValueChange={(val) => { setFilterProvider(val === "all" ? "" : val); setPage(1); }}>
                      <SelectTrigger className="w-[140px] bg-gray-800 border-gray-700 text-white rounded-lg h-9">
                        <SelectValue placeholder={t('vouchers.provider')} />
                      </SelectTrigger>
                      <SelectContent className="bg-gray-900 border-gray-800 text-white shadow-2xl">
                        <SelectItem value="all">{t('vouchers.allProviders')}</SelectItem>
                        {dropdownProviders.map((p: string) => (
                          <SelectItem key={p} value={p}>{p}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>

                    <Select value={filterAmount || "all"} onValueChange={(val) => { setFilterAmount(val === "all" ? "" : val); setPage(1); }}>
                      <SelectTrigger className="w-[100px] bg-gray-800 border-gray-700 text-white rounded-lg h-9">
                        <SelectValue placeholder={t('vouchers.volume')} />
                      </SelectTrigger>
                      <SelectContent className="bg-gray-900 border-gray-800 text-white shadow-2xl">
                        <SelectItem value="all">{t('vouchers.all')}</SelectItem>
                        {dropdownAmounts.sort((a: number, b: number) => a - b).map((a: number) => (
                          <SelectItem key={a} value={a.toString()}>{a} L</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>

                    <DateInput
                      value={filterExpirationDate}
                      onChange={(v) => { setFilterExpirationDate(v); setPage(1); }}
                      className="w-[140px] bg-gray-800 border-gray-700 text-white rounded-lg h-9 text-xs"
                    />

                    {(filterFuelType || filterStatus || filterProvider || filterAmount || filterExpirationDate) && (
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => {
                          setFilterFuelType("");
                          setFilterStatus("");
                          setFilterProvider("");
                          setFilterAmount("");
                          setFilterExpirationDate("");
                          setPage(1);
                        }}
                        title={t('vouchers.clearFilters')}
                        className="h-9 w-9 text-gray-400 hover:bg-gray-800 rounded-lg"
                      >
                        <X className="w-4 h-4" />
                      </Button>
                    )}
                  </div>

                  {selectedVoucherIds.size > 0 ? (
                    <Button variant="destructive" size="sm" onClick={() => setShowDeleteConfirm(true)}>
                      <Trash2 className="w-4 h-4 mr-2" />
                      {t('vouchers.delete')} ({selectedVoucherIds.size})
                    </Button>
                  ) : (
                    vouchers.length > 0 && (
                      <Button variant="ghost" size="sm" onClick={() => setShowDeleteAllConfirm(true)} className="text-red-400 hover:text-red-300 hover:bg-red-900/10">
                        {t('common.deleteAll')} ({globalTotal})
                      </Button>
                    )
                  )}
                </div>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden shadow-xl">
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-gray-800/80 text-gray-400 uppercase text-xs backdrop-blur-sm">
                    <tr>
                      <th className="p-4 w-10 sticky left-0 bg-gray-800/80 z-10">
                        <div
                          className={`w-4 h-4 border rounded cursor-pointer flex items-center justify-center transition-colors ${selectedVoucherIds.size === vouchers.length && vouchers.length > 0 ? 'bg-primary border-primary' : 'border-gray-600 hover:border-gray-500'}`}
                          onClick={toggleSelectAll}
                        >
                          {selectedVoucherIds.size === vouchers.length && vouchers.length > 0 && <CheckSquare className="w-3 h-3 text-black" />}
                        </div>
                      </th>
                      {[
                        { id: 'qrCodeData', label: t('vouchers.image'), sortable: false },
                        { id: 'amount', label: t('vouchers.volume'), sortable: true },
                        { id: 'fuelType', label: t('vouchers.fuelType'), sortable: true },
                        { id: 'provider', label: t('vouchers.provider'), sortable: true },
                        { id: 'expirationDate', label: t('vouchers.expires'), sortable: true },
                        { id: 'externalId', label: t('vouchers.externalId'), sortable: true },
                        { id: 'status', label: t('vouchers.status'), sortable: true },
                        { id: 'createdAt', label: t('common.date'), sortable: true },
                      ].map((col) => (
                        <th
                          key={col.id}
                          className={`text-left p-4 transition-colors ${col.sortable ? 'cursor-pointer hover:text-white hover:bg-white/5' : ''}`}
                          onClick={() => col.sortable && toggleSort(col.id)}
                        >
                          <div className="flex items-center gap-1.5">
                            {col.label}
                            {col.sortable && (
                              sortBy === col.id ? (
                                sortOrder === 'asc' ? <ChevronUp className="w-3.5 h-3.5 text-primary" /> : <ChevronDown className="w-3.5 h-3.5 text-primary" />
                              ) : (
                                <ArrowUpDown className="w-3 h-3 opacity-20" />
                              )
                            )}
                          </div>
                        </th>
                      ))}

                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-800">
                    {vouchers.map((v: VoucherType) => {
                      const statusKey = typeof v.status === 'string' ? voucherStatusKey(v.status) : 'imported';
                      const isSelected = selectedVoucherIds.has(v.id);
                      return (
                        <tr key={v.id} className={`transition-colors ${isSelected ? 'bg-primary/5 hover:bg-primary/10' : 'hover:bg-gray-800/30'}`}>
                          <td className="p-4 sticky left-0 z-10">
                            <div
                              className={`w-4 h-4 border rounded cursor-pointer flex items-center justify-center transition-colors ${isSelected ? 'bg-primary border-primary' : 'border-gray-600 hover:border-gray-500'}`}
                              onClick={() => toggleSelectRow(v.id)}
                            >
                              {isSelected && <CheckSquare className="w-3 h-3 text-black" />}
                            </div>
                          </td>
                          <td className="p-4" onClick={() => setSelectedQrId(v.id)}>
                            <div className="cursor-pointer hover:scale-105 transition-transform bg-white/5 p-1 rounded-md w-fit border border-gray-700">
                              <QrCode className="w-6 h-6 text-gray-400" />
                            </div>
                          </td>
                          <td className="p-4 font-bold text-white">{v.liters} L</td>
                          <td className="p-4">
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-800 text-gray-300 border border-gray-700">
                              {v.fuelType?.name || v.fuelTypeId}
                            </span>
                          </td>
                          <td className="p-4 font-medium text-gray-300 uppercase">{v.provider || "Unknown"}</td>
                          <td className="p-4 text-gray-400 font-mono text-xs">
                            {v.expirationDate ? formatDate(v.expirationDate) : '-'}
                          </td>
                          <td className="p-4 font-mono text-xs text-gray-500">{v.voucherNumber}</td>
                          <td className="p-4">
                            <span className={`px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md ${statusKey === 'available' ? 'bg-green-500/10 text-green-400 border-green-500/20' :
                              statusKey === 'assigned' ? 'bg-blue-500/10 text-blue-400 border-blue-500/20' :
                                statusKey === 'used' ? 'bg-gray-500/10 text-gray-400 border-gray-500/20' :
                                  statusKey === 'sold' ? 'bg-orange-500/10 text-orange-400 border-orange-500/20' :
                                    'bg-yellow-500/10 text-yellow-400 border-yellow-500/20'
                              }`}>
                              {t(`status.${statusKey}`)}
                            </span>
                          </td>
                          <td className="p-4 text-gray-500 text-xs font-mono">
                            {formatDate(v.createdAtUtc || Date.now())}
                          </td>

                        </tr>
                      );
                    })}
                    {isVouchersLoading && (
                      <tr><td colSpan={9} className="p-16 text-center text-gray-500">
                        <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2" />
                        Loading...
                      </td></tr>
                    )}
                    {!isVouchersLoading && vouchers.length === 0 && (
                      <tr><td colSpan={9} className="p-16 text-center text-gray-500 flex flex-col items-center justify-center gap-2">
                        <Package className="w-8 h-8 opacity-20" />
                        {t('import.noVouchers')}
                      </td></tr>
                    )}
                  </tbody>
                </table>
              </div>

              <div className="flex items-center justify-between p-4 border-t border-gray-800 bg-gray-900/50">
                <div className="text-xs text-gray-500">
                  Showing {vouchers.length} items (Page {page})
                </div>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" disabled={page === 1} onClick={() => setPage(p => Math.max(1, p - 1))} className="h-8">
                    <ChevronLeft className="w-4 h-4 mr-1" /> Prev
                  </Button>
                  <Button variant="outline" size="sm" disabled={vouchers.length < limit} onClick={() => setPage(p => p + 1)} className="h-8">
                    Next <ChevronRight className="w-4 h-4 ml-1" />
                  </Button>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Imports Tab */}
        {activeTab === 'imports' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            {selectedImportId ? (
              <>
                {/* Detail View */}
                <div className="flex items-center justify-between">
                  <Button variant="ghost" onClick={() => setSelectedImportId(null)} className="text-gray-400 hover:text-white">
                    <ArrowLeft className="w-4 h-4 mr-2" /> До списку імпортів
                  </Button>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={activateVoucherMutation.isPending}
                      onClick={() => {
                        const ids = importVouchers
                          .filter((v: any) => v.status === 'Imported' || v.status === 'VerifiedWithWarnings' || v.status === 'Expired')
                          .map((v: any) => v.id);
                        if (ids.length > 0) activateVoucherMutation.mutate(ids);
                      }}
                    >
                      <CheckCircle className="w-4 h-4 mr-1" /> Активувати всі
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={expireVoucherMutation.isPending}
                      onClick={() => {
                        const ids = importVouchers
                          .filter((v: any) => v.status === 'Imported' || v.status === 'Available' || v.status === 'VerifiedWithWarnings')
                          .map((v: any) => v.id);
                        if (ids.length > 0) expireVoucherMutation.mutate(ids);
                      }}
                    >
                      <XCircle className="w-4 h-4 mr-1" /> Деактивувати всі
                    </Button>
                  </div>
                </div>

                <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-x-auto">
                  <table className="w-full">
                    <thead className="bg-gray-800">
                      <tr>
                        <th className="text-left p-4">Постачальник</th>
                        <th className="text-left p-4">Паливо</th>
                        <th className="text-left p-4">Об'єм</th>
                        <th className="text-left p-4">Номер</th>
                        <th className="text-left p-4">Термін</th>
                        <th className="text-left p-4">Статус</th>
                        <th className="text-left p-4">Верифікація</th>
                        <th className="text-left p-4">Дії</th>
                      </tr>
                    </thead>
                    <tbody>
                      {importVouchers.map((v: any) => {
                        const statusColors: Record<string, string> = {
                          Imported: 'bg-yellow-500/10 text-yellow-400 border-yellow-500/20',
                          Available: 'bg-green-500/10 text-green-400 border-green-500/20',
                          VerifiedWithWarnings: 'bg-orange-500/10 text-orange-400 border-orange-500/20',
                          VerificationFailed: 'bg-red-500/10 text-red-400 border-red-500/20',
                          Assigned: 'bg-blue-500/10 text-blue-400 border-blue-500/20',
                          Used: 'bg-gray-500/10 text-gray-400 border-gray-500/20',
                          Expired: 'bg-red-500/10 text-red-400 border-red-500/20',
                        };
                        return (
                          <tr key={v.id} className="border-t border-gray-800 hover:bg-gray-800/30">
                            <td className="p-4 uppercase">{v.provider}</td>
                            <td className="p-4">{v.fuelTypeName || v.fuelTypeId}</td>
                            <td className="p-4">{v.liters}L</td>
                            <td className="p-4 font-mono text-xs">{v.voucherNumber}</td>
                            <td className="p-4 text-sm">{v.expirationDate}</td>
                            <td className="p-4">
                              <span className={`px-2 py-1 rounded text-xs font-bold uppercase border ${statusColors[v.status] || 'bg-gray-500/10 text-gray-400'}`}>
                                {t('status.' + v.status.charAt(0).toLowerCase() + v.status.slice(1))}
                              </span>
                            </td>
                            <td className="p-4">
                              {v.verificationMismatchPercent != null ? (
                                <span className={`text-xs font-mono ${
                                  v.verificationMismatchPercent === 0 ? 'text-green-400' :
                                  v.verificationMismatchPercent < 5 ? 'text-yellow-400' : 'text-red-400'
                                }`}>
                                  {v.verificationMismatchPercent.toFixed(2)}% ({v.verificationMismatchedModules}/{v.verificationTotalModules})
                                </span>
                              ) : (
                                <span className="text-xs text-gray-600">—</span>
                              )}
                            </td>
                            <td className="p-4">
                              <div className="flex gap-1">
                                {(v.status === 'Imported' || v.status === 'VerifiedWithWarnings' || v.status === 'Expired') && (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="text-green-400 hover:text-green-300"
                                    disabled={activateVoucherMutation.isPending}
                                    onClick={() => activateVoucherMutation.mutate([v.id])}
                                  >
                                    <CheckCircle className="w-3.5 h-3.5" />
                                  </Button>
                                )}
                                {(v.status === 'Available' || v.status === 'Imported' || v.status === 'VerifiedWithWarnings') && (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="text-red-400 hover:text-red-300"
                                    disabled={expireVoucherMutation.isPending}
                                    onClick={() => expireVoucherMutation.mutate([v.id])}
                                  >
                                    <XCircle className="w-3.5 h-3.5" />
                                  </Button>
                                )}
                              </div>
                            </td>
                          </tr>
                        );
                      })}
                      {importVouchers.length === 0 && (
                        <tr><td colSpan={8} className="p-16 text-center text-gray-500">Немає ваучерів у цьому імпорті</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </>
            ) : (
              <>
                {/* List View */}
                <div className="flex items-center justify-between">
                  <h2 className="text-xl font-bold">{t('imports.title')}</h2>
                </div>
                <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-x-auto">
                  <table className="w-full">
                    <thead className="bg-gray-800">
                      <tr>
                        <th className="text-left p-4">Файл</th>
                        <th className="text-left p-4">Статус</th>
                        <th className="text-left p-4">Сторінок</th>
                        <th className="text-left p-4">Ваучерів</th>
                        <th className="text-left p-4">Імпортовано</th>
                        <th className="text-left p-4">Дублікатів</th>
                        <th className="text-left p-4">Помилок</th>
                        <th className="text-left p-4">Попередж.</th>
                        <th className="text-left p-4">Створено</th>
                        <th className="text-left p-4"></th>
                      </tr>
                    </thead>
                    <tbody>
                      {importsList.map((imp: any) => (
                        <tr key={imp.id} className="border-t border-gray-800 hover:bg-gray-800/30 cursor-pointer" onClick={() => setSelectedImportId(imp.id)}>
                          <td className="p-4 font-medium">{imp.fileName}</td>
                          <td className="p-4">
                            <span className={`px-2 py-1 rounded text-xs font-bold uppercase border ${
                              imp.status === 'Completed' ? 'bg-green-500/10 text-green-400 border-green-500/20' :
                              imp.status === 'Failed' ? 'bg-red-500/10 text-red-400 border-red-500/20' :
                              'bg-yellow-500/10 text-yellow-400 border-yellow-500/20'
                            }`}>
                              {t('status.' + imp.status.toLowerCase())}
                            </span>
                          </td>
                          <td className="p-4">{imp.pageCount}</td>
                          <td className="p-4">{imp.voucherCount}</td>
                          <td className="p-4 text-green-400">{imp.importedCount}</td>
                          <td className="p-4 text-orange-400">{imp.duplicateCount}</td>
                          <td className="p-4 text-red-400">{imp.failedCount + imp.verificationFailedCount}</td>
                          <td className="p-4 text-yellow-400">{imp.verifiedWithWarningsCount}</td>
                          <td className="p-4 text-sm text-gray-400">{formatDateTime(imp.startedAtUtc)}</td>
                          <td className="p-4">
                            <ChevronRight className="w-4 h-4 text-gray-600" />
                          </td>
                        </tr>
                      ))}
                      {importsList.length === 0 && (
                        <tr><td colSpan={10} className="p-16 text-center text-gray-500">Ще немає імпортів</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </div>
        )}

        {/* Reconciliation Tab */}
        {activeTab === 'reconciliation' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <BarChart className="w-6 h-6 text-primary" />
              {t('reconciliation.title')}
            </h2>

            {isReconLoading ? (
              <div className="flex items-center gap-2 text-muted-foreground">
                <Loader2 className="w-5 h-5 animate-spin" />
                {t('common.loading')}
              </div>
            ) : reconciliationData ? (
              <>
                {/* Money Ledger — received / delivered / refunded */}
                <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                  <p className="text-sm text-gray-400 mb-3">{t('reconciliation.moneyLedger')}</p>
                  <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div className="bg-green-900/10 border border-green-800/30 rounded-lg p-3">
                      <p className="text-xs text-gray-500">{t('reconciliation.received')}</p>
                      <p className="text-xl font-bold text-green-400">{((reconciliationData.summary.totalReceivedKopecks ?? 0) / 100).toLocaleString()} ₴</p>
                    </div>
                    <div className="bg-blue-900/10 border border-blue-800/30 rounded-lg p-3">
                      <p className="text-xs text-gray-500">{t('reconciliation.deliveredValue')}</p>
                      <p className="text-xl font-bold text-blue-400">{((reconciliationData.summary.totalFulfilledValueKopecks ?? 0) / 100).toLocaleString()} ₴</p>
                    </div>
                    <div className="bg-red-900/10 border border-red-800/30 rounded-lg p-3">
                      <p className="text-xs text-gray-500">{t('reconciliation.refundedLabel')}</p>
                      <p className="text-xl font-bold text-red-400">{((reconciliationData.summary.totalRefundedKopecks ?? 0) / 100).toLocaleString()} ₴</p>
                      <p className="text-xs text-gray-500">{reconciliationData.summary.refundedOrders ?? 0} {t('reconciliation.refundedOrders')}</p>
                    </div>
                  </div>
                </div>

                {/* Summary Cards — Key Reconciliation Metrics */}
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('report.totalSpent')}</p>
                    <p className="text-2xl font-bold text-green-400">{reconciliationData.summary.totalRevenueKopecks.toLocaleString()} ₴</p>
                    <p className="text-xs text-gray-500">{reconciliationData.summary.fulfilled} {t('report.fulfilledOrders')}</p>
                  </div>
                  <div className={`border rounded-xl p-4 ${reconciliationData.summary.paidUnfulfilled > 0 ? 'bg-red-900/20 border-red-800' : 'bg-gray-900 border-gray-800'}`}>
                    <p className="text-sm text-gray-400">{t('reconciliation.needAttention')}</p>
                    <p className={`text-2xl font-bold ${reconciliationData.summary.paidUnfulfilled > 0 ? 'text-red-400' : 'text-green-400'}`}>
                      {reconciliationData.summary.paidUnfulfilled + reconciliationData.summary.partiallyFulfilled}
                    </p>
                    <p className="text-xs text-gray-500">{reconciliationData.summary.paidUnfulfilled} {t('reconciliation.unfulfilled')} · {reconciliationData.summary.partiallyFulfilled} {t('reconciliation.partial')}</p>
                  </div>
                  <div className={`border rounded-xl p-4 ${reconciliationData.summary.orphanVouchers > 0 || reconciliationData.summary.unprocessedEvents > 10 ? 'bg-yellow-900/20 border-yellow-800' : 'bg-gray-900 border-gray-800'}`}>
                    <p className="text-sm text-gray-400">{t('reconciliation.dataIntegrity')}</p>
                    <p className="text-2xl font-bold text-yellow-400">{reconciliationData.summary.orphanVouchers} / {reconciliationData.summary.unprocessedEvents}</p>
                    <p className="text-xs text-gray-500">{t('reconciliation.orphanVouchers')}</p>
                  </div>
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('reconciliation.providersDeficit')}</p>
                    <p className={`text-2xl font-bold ${reconciliationData.summary.lowInventoryProviders > 0 ? 'text-orange-400' : 'text-green-400'}`}>{reconciliationData.summary.lowInventoryProviders}</p>
                    <p className="text-xs text-gray-500">{reconciliationData.summary.importErrors7d} {t('reconciliation.importErrors7d')}</p>
                  </div>
                </div>

                {/* Exceptions / Issues Section */}
                {reconciliationData.exceptions?.length > 0 && (
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                    <div className="flex items-center gap-2 mb-4">
                      <div className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
                      <h3 className="text-lg font-bold">{t('reconciliation.issuesFound', reconciliationData.exceptions.length.toString())}</h3>
                    </div>
                    <div className="space-y-2">
                      {reconciliationData.exceptions.map((ex: any, i: number) => (
                        <div key={i} className={`flex items-start gap-3 p-3 rounded-lg ${ex.severity === 'critical' ? 'bg-red-900/10 border border-red-800/30' : 'bg-yellow-900/10 border border-yellow-800/30'}`}>
                          <div className={`w-2 h-2 rounded-full mt-1.5 shrink-0 ${ex.severity === 'critical' ? 'bg-red-500' : 'bg-yellow-500'}`} />
                          <div className="flex-1 min-w-0">
                            <p className="text-sm font-medium text-gray-200">{ex.description}</p>
                            <p className="text-xs text-gray-500 mt-0.5">
                              {ex.type} · {formatDate(ex.createdAtUtc)}
                              {ex.id && ex.id !== '00000000-0000-0000-0000-000000000000' && (
                                <span className="ml-2 font-mono">ID: {ex.id.slice(0, 8)}</span>
                              )}
                            </p>
                          </div>
                          <span className={`text-xs px-2 py-0.5 rounded font-medium ${ex.severity === 'critical' ? 'bg-red-500/20 text-red-400' : 'bg-yellow-500/20 text-yellow-400'}`}>
                            {ex.severity === 'critical' ? t('reconciliation.critical') : t('reconciliation.warning')}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Three-Way Match Table: Order ↔ Payment ↔ Fulfillment */}
                <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                  <h3 className="text-lg font-bold mb-4">{t('reconciliation.threeWayMatch')}</h3>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-gray-800">
                        <tr>
                          <th className="text-left p-3">{t('reconciliation.threeOrder')}</th>
                          <th className="text-left p-3">{t('report.provider')}</th>
                          <th className="text-left p-3">{t('report.fuel')}</th>
                          <th className="text-right p-3">{t('report.amountHeader')}</th>
                          <th className="text-left p-3">{t('reconciliation.threePayment')}</th>
                          <th className="text-left p-3">{t('reconciliation.threeFulfillment')}</th>
                          <th className="text-right p-3">{t('reconciliation.threeVouchExp')}</th>
                          <th className="text-right p-3">{t('reconciliation.threeVouchDel')}</th>
                          <th className="text-left p-3">{t('reconciliation.threeMatch')}</th>
                          <th className="text-left p-3">{t('reconciliation.threeRefund')}</th>
                          <th className="text-left p-3">{t('reconciliation.threeAge')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {reconciliationData.threeWayMatch?.map((row: any) => {
                          const matchColor = row.matchStatus === 'OK' ? 'text-green-400 bg-green-500/10' :
                            row.matchStatus === 'PARTIAL' ? 'text-yellow-400 bg-yellow-500/10' :
                            row.matchStatus === 'UNFULFILLED' ? 'text-red-400 bg-red-500/10' :
                            row.matchStatus === 'CANCELLED' ? 'text-gray-500 bg-gray-500/10' :
                            row.matchStatus === 'REFUNDED' ? 'text-red-400 bg-red-500/10' :
                            row.matchStatus === 'PARTIAL_REFUNDED' ? 'text-purple-400 bg-purple-500/10' :
                            'text-blue-400 bg-blue-500/10';
                          return (
                            <tr key={row.orderId} className="border-t border-gray-800">
                              <td className="p-3 font-mono text-xs">{row.orderId.slice(0, 8)}</td>
                              <td className="p-3 uppercase">{row.provider}</td>
                              <td className="p-3">{row.fuelType}</td>
                              <td className="p-3 text-right">{row.totalPrice.toFixed(0)} ₴</td>
                              <td className="p-3">
                                <span className={`px-1.5 py-0.5 rounded text-xs ${row.monobankStatus === 'Success' ? 'bg-green-500/20 text-green-400' : row.monobankStatus === 'Pending' ? 'bg-yellow-500/20 text-yellow-400' : 'bg-gray-500/20 text-gray-400'}`}>
                                  {row.monobankStatus ? t('monobank.status.' + row.monobankStatus.toLowerCase()) : '—'}
                                </span>
                              </td>
                              <td className="p-3 text-xs">{row.orderStatus ? t('order.status.' + row.orderStatus.charAt(0).toLowerCase() + row.orderStatus.slice(1)) : '—'}</td>
                              <td className="p-3 text-right font-mono">{row.vouchersExpected}</td>
                              <td className="p-3 text-right font-mono">{row.vouchersDelivered}</td>
                              <td className="p-3">
                                <span className={`px-1.5 py-0.5 rounded text-xs font-medium ${matchColor}`}>
                                  {t('reconciliation.match' + row.matchStatus.charAt(0) + row.matchStatus.slice(1).toLowerCase())}
                                </span>
                              </td>
                              <td className="p-3">
                                {row.refundStatus ? (
                                  <div className="flex flex-col gap-0.5">
                                    <span className={`px-1.5 py-0.5 rounded text-xs w-fit ${row.refundStatus === 'Completed' ? 'bg-green-500/20 text-green-400' : row.refundStatus === 'Failed' ? 'bg-red-500/20 text-red-400' : 'bg-yellow-500/20 text-yellow-400'}`}>
                                      {t('purchases.refundStatus.' + row.refundStatus)}
                                    </span>
                                    {row.refundedKopecks > 0 && (
                                      <span className="text-xs font-mono text-gray-400">{(row.refundedKopecks / 100).toLocaleString()} ₴</span>
                                    )}
                                  </div>
                                ) : '—'}
                              </td>
                              <td className="p-3 text-xs text-gray-400">{row.daysSinceCreated}d</td>
                            </tr>
                          );
                        })}
                        {(!reconciliationData.threeWayMatch || reconciliationData.threeWayMatch.length === 0) && (
                          <tr><td colSpan={11} className="p-8 text-center text-gray-500">{t('reconciliation.noOrderData')}</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                {/* Voucher Funnel */}
                <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                  <h3 className="text-lg font-bold mb-4">{t('reconciliation.voucherFunnel')}</h3>
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                    {reconciliationData.voucherFunnel?.map((item: any) => {
                      const colors: Record<string, string> = {
                        Imported: 'bg-blue-500/10 border-blue-500/30 text-blue-400',
                        Available: 'bg-green-500/10 border-green-500/30 text-green-400',
                        Assigned: 'bg-yellow-500/10 border-yellow-500/30 text-yellow-400',
                        Used: 'bg-purple-500/10 border-purple-500/30 text-purple-400',
                        Expired: 'bg-gray-500/10 border-gray-500/30 text-gray-400',
                        VerificationFailed: 'bg-red-500/10 border-red-500/30 text-red-400',
                      };
                      const c = colors[item.status] || 'bg-gray-500/10 border-gray-500/30 text-gray-400';
                      return (
                        <div key={item.status} className={`border rounded-lg p-4 text-center ${c}`}>
                          <p className="text-2xl font-bold">{item.count}</p>
                          <p className="text-xs mt-1">{t('status.' + item.status.charAt(0).toLowerCase() + item.status.slice(1))}</p>
                          <p className="text-xs opacity-60">{item.totalLiters}L</p>
                        </div>
                      );
                    })}
                  </div>
                </div>

                {/* Profit Summary */}
                {reconciliationData.revenueSummary?.length > 0 && (
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                    <h3 className="text-lg font-bold mb-4">{t('report.profitSummary')}</h3>
                    <div className="overflow-x-auto">
                      <table className="w-full text-sm">
                        <thead className="bg-gray-800">
                          <tr>
                            <th className="text-left p-3">{t('report.period')}</th>
                            <th className="text-right p-3">{t('report.orders_short')}</th>
                            <th className="text-right p-3">{t('report.profit')}</th>
                            <th className="text-right p-3">{t('report.avgOrder')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {reconciliationData.revenueSummary.map((m: any, i: number) => (
                            <tr key={i} className="border-t border-gray-800">
                              <td className="p-3">{m.year}-{String(m.month).padStart(2, '0')}</td>
                              <td className="p-3 text-right font-mono">{m.orderCount}</td>
                              <td className="p-3 text-right font-mono text-green-400">{m.revenueKopecks.toLocaleString()} ₴</td>
                              <td className="p-3 text-right font-mono text-gray-400">{m.orderCount > 0 ? `${(m.revenueKopecks / m.orderCount).toLocaleString()} ₴` : '—'}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div className="text-center text-gray-500 py-12">{t('reconciliation.loadFailed')}</div>
            )}
          </div>
        )}

        {/* Audit Log Tab */}
        {activeTab === 'auditlog' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <ScrollText className="w-6 h-6 text-primary" />
              {t('auditlog.title')}
            </h2>
            <AuditTab />
          </div>
        )}

        {/* Error Logs Tab */}
        {activeTab === 'errorlogs' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <Bug className="w-6 h-6 text-red-400" />
              {t('errorlogs.title')}
            </h2>
            <ErrorLogsTab />
          </div>
        )}

        {/* Settings Tab */}
        {activeTab === 'settings' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <SettingsTab />
          </div>
        )}

        {/* Reports Tab */}
        {activeTab === 'reports' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <BarChart className="w-6 h-6 text-primary" />
              {t('report.title')}
            </h2>

            {/* Filters */}
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 flex flex-wrap gap-4 items-end">
              <div>
                <label className="text-xs text-gray-400 block mb-1">{t('report.allUsers')}</label>
                <div className="flex items-center gap-2">
                  <select
                    value={reportUserId}
                    onChange={(e) => setReportUserId(e.target.value)}
                    style={{ colorScheme: "dark" }}
                    className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white w-64 [&>option]:bg-gray-900 [&>option]:text-white"
                  >
                    <option value="">{t('report.allUsers')}</option>
                    {usersList.map((u: UserType) => {
                      const label = u.firstName || u.lastName
                        ? `${u.firstName || ''} ${u.lastName || ''}`.trim()
                        : u.phone || u.id?.slice(0, 8) || u.id;
                      return (
                        <option key={u.id} value={u.id}>
                          {label}
                        </option>
                      );
                    })}
                  </select>
                  <span className="text-xs text-gray-500">({usersList.length} users loaded)</span>
                </div>
              </div>
              <div>
                <label className="text-xs text-gray-400 block mb-1">{t('report.from')}</label>
                <input
                  type="text"
                  placeholder="dd.mm.yyyy"
                  value={reportFromDate ? formatDisplayDate(reportFromDate) : ''}
                  onChange={(e) => {
                    const v = e.target.value.replace(/[^\d]/g, '');
                    if (v.length === 8) setReportFromDate(`${v.slice(4,8)}-${v.slice(2,4)}-${v.slice(0,2)}`);
                    else if (v.length < 8) setReportFromDate('');
                  }}
                  className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white"
                />
              </div>
              <div>
                <label className="text-xs text-gray-400 block mb-1">{t('report.to')}</label>
                <input
                  type="text"
                  placeholder="dd.mm.yyyy"
                  value={reportToDate ? formatDisplayDate(reportToDate) : ''}
                  onChange={(e) => {
                    const v = e.target.value.replace(/[^\d]/g, '');
                    if (v.length === 8) setReportToDate(`${v.slice(4,8)}-${v.slice(2,4)}-${v.slice(0,2)}`);
                    else if (v.length < 8) setReportToDate('');
                  }}
                  className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white"
                />
              </div>
              <div className="flex gap-1 items-end">
                <Button onClick={() => setReportQuickDate('yesterday')} variant="outline" size="sm" className="h-9 text-xs">{t('report.yesterday')}</Button>
                <Button onClick={() => setReportQuickDate('week')} variant="outline" size="sm" className="h-9 text-xs">{t('report.week')}</Button>
                <Button onClick={() => setReportQuickDate('30days')} variant="outline" size="sm" className="h-9 text-xs">{t('report.30days')}</Button>
                <Button onClick={() => setReportQuickDate('month')} variant="outline" size="sm" className="h-9 text-xs">{t('report.month')}</Button>
              </div>
              <div className="flex gap-2 items-end">
                <Button onClick={() => setReportTrigger(t => t + 1)} variant="default" size="sm" className="gap-2">
                  <BarChart className="w-4 h-4" />
                  {t('report.generate')}
                </Button>
                {reportData && (
                  <Button onClick={() => setShowReconciliationAct(true)} variant="outline" size="sm" className="gap-2">
                    <FileSignature className="w-4 h-4" />
                    {t('report.reconciliationAct')}
                  </Button>
                )}
              </div>
            </div>

            {isReportLoading ? (
              <div className="flex items-center gap-2 text-muted-foreground">
                <Loader2 className="w-5 h-5 animate-spin" />
                {t('report.loading')}
              </div>
            ) : reportData ? (
              <>
                {/* Summary Cards */}
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('report.totalSpent')}</p>
                    <p className="text-2xl font-bold text-green-400">{reportData.summary.totalSpent.toLocaleString()} ₴</p>
                    <p className="text-xs text-gray-500">{reportData.summary.totalOrders} {t('report.orders')}</p>
                  </div>
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('report.purchased')}</p>
                    <p className="text-2xl font-bold text-yellow-400">{reportData.summary.vouchersPurchased}</p>
                    <p className="text-xs text-gray-500">{t('report.litersPurchased', reportData.summary.totalLitersPurchased.toFixed(0))}</p>
                  </div>
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('report.used')}</p>
                    <p className="text-2xl font-bold text-red-400">{reportData.summary.vouchersUsed}</p>
                  </div>
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-4">
                    <p className="text-sm text-gray-400">{t('report.litersUsed')}</p>
                    <p className="text-2xl font-bold text-blue-400">{reportData.summary.totalLitersUsed.toFixed(0)} L</p>
                  </div>
                </div>

                {/* Monthly Breakdown */}
                {reportData.monthlyBreakdown?.length > 0 && (
                  <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                    <h3 className="text-lg font-bold mb-4">{t('report.monthlyBreakdown')}</h3>
                    <div className="overflow-x-auto">
                      <table className="w-full text-sm">
                        <thead className="bg-gray-800">
                          <tr>
                            <th className="text-left p-3">{t('report.month')}</th>
                            <th className="text-right p-3">{t('report.amount')}</th>
                            <th className="text-right p-3">{t('report.purchased')}</th>
                            <th className="text-right p-3">{t('report.used')}</th>
                            <th className="text-right p-3">{t('report.litersUsed')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {reportData.monthlyBreakdown.map((mb: any) => (
                            <tr key={mb.month} className="border-t border-gray-800">
                              <td className="p-3 font-medium">{mb.month}</td>
                              <td className="p-3 text-right text-green-400">{mb.totalSpent.toLocaleString()} ₴</td>
                              <td className="p-3 text-right text-yellow-400">{mb.vouchersPurchased}</td>
                              <td className="p-3 text-right text-red-400">{mb.vouchersUsed}</td>
                              <td className="p-3 text-right text-blue-400">{mb.totalLitersUsed.toFixed(0)}L</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}

                {/* Payments Table */}
                <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                  <h3 className="text-lg font-bold mb-4">{t('report.payments', reportData.payments.length)}</h3>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-gray-800">
                        <tr>
                          <th className="text-left p-3">{t('report.paymentId')}</th>
                          <th className="text-left p-3">{t('report.provider')}</th>
                          <th className="text-left p-3">{t('report.fuel')}</th>
                          <th className="text-right p-3">{t('report.amountHeader')}</th>
                          <th className="text-right p-3">{t('report.liters')}</th>
                          <th className="text-right p-3">{t('report.quantity')}</th>
                          <th className="text-left p-3">{t('report.bank')}</th>
                          <th className="text-left p-3">{t('report.status')}</th>
                          <th className="text-left p-3">{t('report.date')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {reportData.payments.map((p: any) => (
                          <tr key={p.orderId} className="border-t border-gray-800">
                            <td className="p-3 font-mono text-xs text-gray-400">{p.orderId.slice(0, 8)}</td>
                            <td className="p-3 uppercase text-xs">{p.provider || '—'}</td>
                            <td className="p-3">{p.fuelType || '—'}</td>
                            <td className="p-3 text-right font-mono">{p.amount.toLocaleString()} ₴</td>
                            <td className="p-3 text-right">{(p.liters / p.quantity)}L</td>
                            <td className="p-3 text-right">{p.quantity}</td>
                            <td className="p-3">
                              {p.monobankStatus ? (
                                <span className={`px-1.5 py-0.5 rounded text-xs ${
                                  p.monobankStatus === 'Success' ? 'bg-green-500/20 text-green-400' :
                                  'bg-gray-500/20 text-gray-400'
                                }`}>{t('monobank.status.' + p.monobankStatus.toLowerCase())}</span>
                              ) : (
                                <span className="text-xs text-gray-500">—</span>
                              )}
                            </td>
                            <td className="p-3">
                              <span className={`px-1.5 py-0.5 rounded text-xs ${
                                p.status === 'Fulfilled' ? 'bg-green-500/20 text-green-400' :
                                p.status === 'Cancelled' || p.status === 'Refunded' ? 'bg-red-500/20 text-red-400' :
                                'bg-yellow-500/20 text-yellow-400'
                              }`}>{t('order.status.' + p.status.charAt(0).toLowerCase() + p.status.slice(1))}</span>
                            </td>
                            <td className="p-3 text-xs text-gray-400">{formatDate(p.createdAtUtc)}</td>
                          </tr>
                        ))}
                        {reportData.payments.length === 0 && (
                          <tr><td colSpan={9} className="p-8 text-center text-gray-500">{t('report.noPayments')}</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                  {reportData.payments.length > 0 && (
                    <div className="mt-4 text-xs text-gray-500">
                      {reportData.payments.map((p: any) => p.monobankInvoiceId).filter(Boolean).length > 0 && (
                        <span>{t('report.monobankInvoices', reportData.payments.filter((p: any) => p.monobankInvoiceId).length)}</span>
                      )}
                    </div>
                  )}
                </div>

                {/* Redemptions Table */}
                <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
                  <h3 className="text-lg font-bold mb-4">{t('report.redemptions', reportData.redemptions.length)}</h3>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-gray-800">
                        <tr>
                          <th className="text-left p-3">{t('report.redemptionId')}</th>
                          <th className="text-left p-3">{t('report.provider')}</th>
                          <th className="text-left p-3">{t('report.fuelName')}</th>
                          <th className="text-right p-3">{t('report.liters')}</th>
                          <th className="text-left p-3">{t('report.redeemedAt')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {reportData.redemptions.map((r: any) => (
                          <tr key={r.voucherId} className="border-t border-gray-800">
                            <td className="p-3 font-mono text-xs text-gray-400">{r.voucherId.slice(0, 8)}</td>
                            <td className="p-3 uppercase">{r.provider || '—'}</td>
                            <td className="p-3">{r.fuelName || r.fuelType || '—'}</td>
                            <td className="p-3 text-right">{r.liters}L</td>
                            <td className="p-3 text-xs text-gray-400">{formatDate(r.redeemedAt)}</td>
                          </tr>
                        ))}
                        {reportData.redemptions.length === 0 && (
                          <tr><td colSpan={5} className="p-8 text-center text-gray-500">{t('report.noRedemptions')}</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </>
            ) : (
              <div className="text-center text-gray-500 py-12">{t('report.noData')}</div>
            )}
          </div>
        )}

        {/* Contracts Tab */}
        {activeTab === 'contracts' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">ДОСТУПНІ ДОГОВОРИ</h2>
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gray-800">
                    <tr>
                      <th className="text-left p-4">Назва</th>
                      <th className="text-left p-4">Версія</th>
                      <th className="text-left p-4">Статус</th>
                      <th className="text-left p-4">Дата створення</th>
                    </tr>
                  </thead>
                  <tbody>
                    {contractsList.map((contract: ContractType) => (
                      <tr key={contract.id} className="border-t border-gray-800">
                        <td className="p-4 font-bold">{contract.title}</td>
                        <td className="p-4 font-mono">{contract.version}</td>
                        <td className="p-4">
                          <span className={`px-2 py-1 rounded text-xs ${contract.status === "ACTIVE" ? "bg-green-500/20 text-green-400" : "bg-gray-500/20 text-gray-400"}`}>
                            {t('status.' + contract.status.toLowerCase())}
                          </span>
                        </td>
                        <td className="p-4 text-gray-400">
                          {formatDate(contract.createdAt)}
                        </td>
                      </tr>
                    ))}
                    {contractsList.length === 0 && (
                      <tr><td colSpan={4} className="p-8 text-center text-gray-500">Додаткових договорів не знайдено</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">ПІДПИСАНІ ДОГОВОРИ</h2>
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gray-800">
                    <tr>
                      <th className="text-left p-4">Користувач</th>
                      <th className="text-left p-4">Компанія</th>
                      <th className="text-left p-4">Договір</th>
                      <th className="text-left p-4">Дата підпису</th>
                      <th className="text-left p-4">Підпис</th>
                    </tr>
                  </thead>
                  <tbody>
                    {signedContractsList.map((sc: UserContractType) => (
                      <tr key={sc.id} className="border-t border-gray-800">
                        <td className="p-4">{sc.userName}</td>
                        <td className="p-4">{sc.companyName}</td>
                        <td className="p-4">{sc.contractTitle}</td>
                        <td className="p-4 text-gray-400">
                          {formatDateTime(sc.signedAt)}
                        </td>
                        <td className="p-4">
                          <Button 
                            variant="ghost" 
                            size="sm"
                            onClick={() => setSelectedSignature(sc.signatureData)}
                          >
                            <FileSignature className="w-4 h-4 mr-2" />
                            Переглянути
                          </Button>
                        </td>
                      </tr>
                    ))}
                    {signedContractsList.length === 0 && (
                      <tr><td colSpan={5} className="p-8 text-center text-gray-500">Підписаних договорів ще немає</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}
      </div>

      {selectedQrData && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4" onClick={() => setSelectedQrId(null)}>
          <div className="bg-white p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={(e: React.MouseEvent) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-black mb-4">{t('import.scanTitle')}</h3>

            {isVoucherLoading ? (
              <div className="w-full h-64 flex items-center justify-center">
                <Loader2 className="w-8 h-8 animate-spin text-primary" />
              </div>
            ) : (
              <>
                <div className="w-full h-64 bg-white flex items-center justify-center mb-4 rounded-lg border-2 border-dashed border-gray-200">
                  <img src={fullVoucherData?.qrImage} alt="QR" style={{ width: 200, height: 200, imageRendering: 'pixelated' }} />
                </div>
                <p className="font-mono text-xs break-all text-gray-500 mb-4 bg-gray-100 p-2 rounded">{fullVoucherData?.qrPayload}</p>
              </>
            )}

            <Button className="w-full font-bold" onClick={() => setSelectedQrId(null)}>{t('common.close')}</Button>
          </div>
        </div>
      )}

      {selectedSignature && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4" onClick={() => setSelectedSignature(null)}>
          <div className="bg-gray-900 border border-gray-800 p-6 rounded-lg max-w-lg w-full animate-in zoom-in-50 duration-200" onClick={(e: React.MouseEvent) => e.stopPropagation()}>
            <div className="flex justify-between items-center mb-4">
                <h3 className="text-xl font-bold text-white">╨ƒ╨ò╨á╨ò╨ô╨¢╨»╨ö ╨ƒ╨å╨ö╨ƒ╨ÿ╨í╨ú</h3>
                <Button variant="ghost" size="sm" onClick={() => setSelectedSignature(null)}>
                    <X className="w-5 h-5" />
                </Button>
            </div>
            
            <div className="bg-white rounded-lg p-4 mb-6">
                <SignatureViewer data={selectedSignature} />
            </div>

            <Button className="w-full font-bold" onClick={() => setSelectedSignature(null)}>╨ù╨É╨Ü╨á╨ÿ╨ó╨ÿ</Button>
          </div>
        </div>
      )}

      {showDeleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4">
          <div className="bg-gray-900 border border-gray-800 p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={(e: React.MouseEvent) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-2">{t('vouchers.deleteConfirmTitle')}</h3>
            <p className="text-gray-400 mb-6">
              {t('vouchers.deleteConfirm', selectedVoucherIds.size.toString())}
            </p>
            <div className="flex gap-2">
              <Button variant="outline" className="flex-1" onClick={() => setShowDeleteConfirm(false)}>{t('vouchers.cancel')}</Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => {
                  deleteSelectedMutation.mutate(Array.from(selectedVoucherIds));
                  setShowDeleteConfirm(false);
                }}
              >
                {t('vouchers.delete')}
              </Button>
            </div>
          </div>
        </div>
      )}

      {showDeleteAllConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4">
          <div className="bg-gray-900 border border-gray-800 p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={(e: any) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-2">{t('vouchers.deleteAllTitle')}</h3>
            <p className="text-gray-400 mb-6">
              {t('vouchers.deleteAllConfirm', globalTotal.toString())}
            </p>
            <div className="flex gap-2">
              <Button variant="outline" className="flex-1" onClick={() => setShowDeleteAllConfirm(false)}>{t('vouchers.cancel')}</Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => {
                  bulkDeleteMutation.mutate();
                  setShowDeleteAllConfirm(false);
                }}
              >
                {t('vouchers.deleteAllAction')}
              </Button>
            </div>
          </div>
        </div>
      )}

      {refundTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4">
          <div className="bg-gray-900 border border-gray-800 p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={(e: React.MouseEvent) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-2">{t('purchases.refundTitle')}</h3>
            <p className="text-gray-400 mb-2">
              {t('purchases.refundConfirm', (refundTarget.refundableAmountKopecks / 100).toFixed(2))}
            </p>
            {refundTarget.refundableAmountKopecks <= 0 && (
              <p className="text-amber-400 text-sm mb-6">{t('purchases.refundNothingToRefund')}</p>
            )}
            <div className="flex gap-2">
              <Button variant="outline" className="flex-1" onClick={() => setRefundTarget(null)}>{t('vouchers.cancel')}</Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => refundPurchaseMutation.mutate(refundTarget.id)}
                disabled={refundPurchaseMutation.isPending || refundTarget.refundableAmountKopecks <= 0}
              >
                {t('purchases.refund')}
              </Button>
            </div>
          </div>
        </div>
      )}

      {showReconciliationAct && reportData && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 overflow-auto">
          <div className="bg-white text-black rounded-xl p-8 max-w-4xl w-full animate-in zoom-in-50 duration-200 max-h-[95vh] overflow-y-auto print:shadow-none print:rounded-none print:p-4 print:max-h-none" onClick={(e: React.MouseEvent) => e.stopPropagation()}>
            <style>{`@media print { .no-print { display: none !important; } body { background: white; } }`}</style>
            <div className="flex justify-between items-center mb-6 no-print">
              <h2 className="text-xl font-bold">{t('reconciliation.modalTitle')}</h2>
              <div className="flex gap-2">
                <Button onClick={() => window.print()} variant="default" size="sm" className="bg-blue-600 text-white hover:bg-blue-700">
                  {t('common.print')}
                </Button>
                <Button onClick={() => setShowReconciliationAct(false)} variant="outline" size="sm">{t('common.close')}</Button>
              </div>
            </div>

            <div className="text-center mb-8">
              <h1 className="text-2xl font-bold uppercase">{t('reconciliation.documentTitle')}</h1>
              <p className="text-gray-600 mt-1">
                {t('reconciliation.periodLabel')} {reportData.period.from ? formatDate(reportData.period.from) : t('reconciliation.periodFrom')}
                {' — '}
                {reportData.period.to ? formatDate(reportData.period.to) : t('reconciliation.periodTo')}
              </p>
              {(() => {
                const u = reportUserId ? usersList.find((x: UserType) => x.id === reportUserId) : null;
                if (!u) return null;
                return (
                  <p className="text-gray-700 mt-2 font-medium">
                    {t('reconciliation.userLabel')} {u.firstName || ''} {u.lastName || ''} ({u.phone || ''})
                  </p>
                );
              })()}
            </div>

            <table className="w-full border-collapse mb-6">
              <thead>
                <tr className="bg-gray-100">
                  <th className="border border-gray-300 p-2 text-left text-sm">{t('table.number')}</th>
                  <th className="border border-gray-300 p-2 text-left text-sm">{t('table.date')}</th>
                  <th className="border border-gray-300 p-2 text-left text-sm">{t('table.description')}</th>
                  <th className="border border-gray-300 p-2 text-right text-sm">{t('table.debit')}</th>
                  <th className="border border-gray-300 p-2 text-right text-sm">{t('table.credit')}</th>
                </tr>
              </thead>
              <tbody>
                {reportData.payments.length === 0 && reportData.redemptions.length === 0 ? (
                  <tr><td colSpan={5} className="border border-gray-300 p-4 text-center text-gray-500">{t('reconciliation.noOperations')}</td></tr>
                ) : (
                  <>
                    {(() => {
                      const totalPaid = reportData.payments.reduce((s: number, p: any) => s + p.amount, 0);
                      const totalLiters = reportData.payments.reduce((s: number, p: any) => s + p.liters, 0);
                      const avgPrice = totalLiters > 0 ? totalPaid / totalLiters : 0;
                      const rows: { type: string; date: string; desc: string; debit: number | null; credit: number | null; id: string }[] = [];
                      reportData.payments.forEach((p: any) => {
                        rows.push({
                          type: 'payment', id: p.orderId, date: p.createdAtUtc,
                          desc: t('reconciliation.paymentDesc', p.fuelType || t('reconciliation.fuel'), (p.liters / p.quantity).toString(), p.quantity.toString()),
                          debit: p.amount, credit: null,
                        });
                        // Confirmed refund = money returned for undelivered vouchers.
                        if (p.refundStatus === 'Completed' && p.refundedKopecks > 0) {
                          rows.push({
                            type: 'refund', id: p.orderId + '-refund', date: p.createdAtUtc,
                            desc: t('reconciliation.refundDesc', p.fuelType || t('reconciliation.fuel')),
                            debit: null, credit: p.refundedKopecks / 100,
                          });
                        }
                      });
                      reportData.redemptions.forEach((r: any) => {
                        rows.push({
                          type: 'redemption', id: r.voucherId, date: r.redeemedAt,
                          desc: t('reconciliation.redemptionDesc', r.fuelName || r.fuelType || t('reconciliation.fuel'), r.liters.toString()),
                          debit: null, credit: r.liters * avgPrice,
                        });
                      });
                      rows.sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
                      let totalDebit = 0, totalCredit = 0;
                      return rows.map((row, i) => {
                        if (row.debit !== null) totalDebit += row.debit;
                        if (row.credit !== null) totalCredit += row.credit;
                        return (
                          <tr key={row.id}>
                            <td className="border border-gray-300 p-2 text-sm font-mono">{i + 1}</td>
                            <td className="border border-gray-300 p-2 text-sm">{formatDate(row.date)}</td>
                            <td className="border border-gray-300 p-2 text-sm">{row.desc}</td>
                            <td className="border border-gray-300 p-2 text-sm text-right font-mono">{row.debit !== null ? row.debit.toLocaleString() : '—'}</td>
                            <td className="border border-gray-300 p-2 text-sm text-right font-mono">{row.credit !== null ? row.credit.toLocaleString() : '—'}</td>
                          </tr>
                        );
                      });
                    })()}
                  </>
                )}
              </tbody>
              {reportData.payments.length > 0 || reportData.redemptions.length > 0 ? (
                <tfoot>
                  {(() => {
                    const totalPaid = reportData.payments.reduce((s: number, p: any) => s + p.amount, 0);
                    const totalLiters = reportData.payments.reduce((s: number, p: any) => s + p.liters, 0);
                    const avgPrice = totalLiters > 0 ? totalPaid / totalLiters : 0;
                    let totalDebit = reportData.payments.reduce((s: number, p: any) => s + p.amount, 0);
                    let totalCredit = reportData.redemptions.reduce((s: number, r: any) => s + r.liters * avgPrice, 0);
                    const totalRefunded = reportData.payments.reduce((s: number, p: any) =>
                      s + (p.refundStatus === 'Completed' ? (p.refundedKopecks ?? 0) / 100 : 0), 0);
                    const totalDelivered = reportData.payments.reduce((s: number, p: any) =>
                      s + ((p.fulfilledValueKopecks ?? 0) / 100), 0);
                    totalCredit += totalRefunded;
                    const balance = totalDebit - totalCredit;
                    return (
                      <>
                        <tr className="font-bold bg-gray-50">
                          <td colSpan={3} className="border border-gray-300 p-2 text-sm text-right">{t('reconciliation.total')}</td>
                          <td className="border border-gray-300 p-2 text-sm text-right font-mono">{totalDebit.toLocaleString()}</td>
                          <td className="border border-gray-300 p-2 text-sm text-right font-mono">{totalCredit.toLocaleString()}</td>
                        </tr>
                        <tr className="text-gray-600">
                          <td colSpan={4} className="border border-gray-300 p-2 text-sm text-right">{t('reconciliation.received')}</td>
                          <td className="border border-gray-300 p-2 text-sm text-right font-mono">{totalDebit.toLocaleString()} {t('table.uah')}</td>
                        </tr>
                        <tr className="text-gray-600">
                          <td colSpan={4} className="border border-gray-300 p-2 text-sm text-right">{t('reconciliation.deliveredValue')}</td>
                          <td className="border border-gray-300 p-2 text-sm text-right font-mono">{totalDelivered.toLocaleString()} {t('table.uah')}</td>
                        </tr>
                        <tr className="text-gray-600">
                          <td colSpan={4} className="border border-gray-300 p-2 text-sm text-right">{t('reconciliation.refundedLabel')}</td>
                          <td className="border border-gray-300 p-2 text-sm text-right font-mono">{totalRefunded.toLocaleString()} {t('table.uah')}</td>
                        </tr>
                        <tr className="font-bold bg-blue-50">
                          <td colSpan={4} className="border border-gray-300 p-2 text-sm text-right">{t('reconciliation.balance')}</td>
                          <td className={`border border-gray-300 p-2 text-sm text-right font-mono ${balance >= 0 ? 'text-green-700' : 'text-red-700'}`}>{balance.toLocaleString()} {t('table.uah')}</td>
                        </tr>
                      </>
                    );
                  })()}
                </tfoot>
              ) : null}
            </table>

            <div className="grid grid-cols-2 gap-8 mt-10 text-sm">
              <div>
                <p className="font-medium mb-8">{t('reconciliation.adminSignature')}</p>
                <div className="border-b border-gray-400 mt-10" />
              </div>
              <div>
                <p className="font-medium mb-8">{t('reconciliation.userSignature')}</p>
                <div className="border-b border-gray-400 mt-10" />
              </div>
            </div>
          </div>
        </div>
      )}

    </Layout>
  );
}

const SignatureViewer = ({ data }: { data: string }) => {
    try {
      const paths = JSON.parse(data);
      if (!Array.isArray(paths)) return <div className="text-red-500">Invalid signature data</div>;
      
      return (
        <svg viewBox="0 0 400 200" className="w-full h-auto" style={{ maxHeight: '300px' }}>
          {paths.map((d: string, i: number) => (
            <path 
                key={i} 
                d={d} 
                fill="none" 
                stroke="#22c55e" 
                strokeWidth="4" 
                strokeLinecap="round" 
                strokeLinejoin="round" 
            />
          ))}
        </svg>
      );
    } catch (e) {
      return <div className="p-4 text-red-500 text-center">╨ƒ╨╛╨╝╨╕╨╗╨║╨░ ╨╖╨░╨▓╨░╨╜╤é╨░╨╢╨╡╨╜╨╜╤Å ╨┐╤û╨┤╨┐╨╕╤ü╤â</div>;
    }
};
