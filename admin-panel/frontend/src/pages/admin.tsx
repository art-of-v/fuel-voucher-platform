import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Trash2, Package, QrCode, ShoppingCart, Plus, Building, Fuel, Edit2, FileUp, Loader2, ChevronUp, ChevronDown, Filter, CheckSquare, Square, ArrowUpDown, ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/utils";
import { Layout } from "@/components/layout";
import { useI18n } from "@/lib/i18n";
import { QRCodeCanvas } from "qrcode.react";
// Remove unused import if present, assuming toast is not used or handled differently
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
  const [selectedQrData, setSelectedQrData] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);

  // Enhanced Voucher State
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("desc");
  const [filterFuelType, setFilterFuelType] = useState("");
  const [selectedVoucherIds, setSelectedVoucherIds] = useState<Set<string>>(new Set());
  const [page, setPage] = useState(1);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
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
    qrCodeUrl?: string; // Add optional qrCodeUrl
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

  const { data: stationsList = [] } = useQuery<StationType[]>({
    queryKey: ["/api/admin/stations"],
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

  const { data: vouchersResponse, refetch: refetchVouchers } = useQuery<any>({
    queryKey: ["/api/vouchers", page, sortBy, sortOrder, filterFuelType],
    queryFn: async () => {
      const params = new URLSearchParams({
        page: page.toString(),
        limit: limit.toString(),
        sortBy,
        sortDirection: sortOrder,
        ...(filterFuelType ? { fuelType: filterFuelType } : {})
      });
      const res = await apiRequest("GET", `/api/vouchers?${params.toString()}`);
      return res.json();
    }
  });

  const vouchers = vouchersResponse?.data || [];
  const totalVouchers = vouchersResponse?.total || 0;
  const globalTotal = vouchersResponse?.globalTotal || 0;
  const dropdownFuelTypes = vouchersResponse?.fuelTypes || [];

  const { data: packages = [] } = useQuery<PackageType[]>({
    queryKey: ["/api/admin/packages"],
  });

  const createStationMutation = useMutation({
    mutationFn: async (data: typeof newStation) => {
      const res = await apiRequest("POST", "/api/admin/stations", data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
      setNewStation({ id: "", name: "", color: "#00ff80", logoText: "" });
    },
  });

  const deleteStationMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/admin/stations/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
    },
  });

  const createFuelTypeMutation = useMutation({
    mutationFn: async (data: typeof newFuelType) => {
      const res = await apiRequest("POST", "/api/admin/fuel-types", data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
      setNewFuelType({ id: "", name: "", stationId: "", basePrice: 0, discountPrice: 0 });
    },
  });

  const deleteFuelTypeMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/admin/fuel-types/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
    },
  });

  const updateStationMutation = useMutation({
    mutationFn: async (data: StationType) => {
      const res = await apiRequest("PUT", `/api/admin/stations/${data.id}`, data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/stations"] });
      setEditingStation(null);
    },
  });

  const updateFuelTypeMutation = useMutation({
    mutationFn: async (data: FuelTypeType) => {
      const res = await apiRequest("PUT", `/api/admin/fuel-types/${data.id}`, data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-types"] });
      setEditingFuelType(null);
    },
  });

  const createQrMutation = useMutation({
    mutationFn: async (data: typeof newQr) => {
      const res = await apiRequest("POST", "/api/qr-codes", data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/qr-codes"] });
      setNewQr({ ...newQr, qrCodeUrl: "" });
    },
  });

  const deleteQrMutation = useMutation({
    mutationFn: async (id: number) => {
      await apiRequest("DELETE", `/api/admin/qr-codes/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/qr-codes"] });
    },
  });

  const createPackageMutation = useMutation({
    mutationFn: async (data: typeof newPackage) => {
      const res = await apiRequest("POST", "/api/admin/packages", data);
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/packages"] });
      setNewPackage({ id: "", stationId: "", fuelTypeId: "", fuelName: "", liters: 10, price: 0, originalPrice: 0 });
    },
  });

  const deletePackageMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/admin/packages/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/packages"] });
    },
  });



  const bulkDeleteMutation = useMutation({
    mutationFn: async () => {
      await apiRequest("POST", "/api/vouchers/bulk-action", { action: "delete_all", ids: [] });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/vouchers"] });
    },
  });

  const deleteSelectedMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest("POST", "/api/vouchers/bulk-action", { action: "delete", ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/vouchers"] });
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

  const toggleSelectAll = () => {
    if (selectedVoucherIds.size === vouchers.length && vouchers.length > 0) {
      setSelectedVoucherIds(new Set());
    } else {
      const newSet = new Set(selectedVoucherIds);
      vouchers.forEach((v: any) => newSet.add(v.id));
      setSelectedVoucherIds(newSet);
    }
  };

  const toggleSelectRow = (id: string) => {
    const newSet = new Set(selectedVoucherIds);
    if (newSet.has(id)) newSet.delete(id);
    else newSet.add(id);
    setSelectedVoucherIds(newSet);
  };

  const availableQrs = qrCodes.filter(q => q.status === "available");
  const soldQrs = qrCodes.filter(q => q.status === "sold");

  return (
    <Layout activeTab={activeTab} onTabChange={setActiveTab}>
      <div className="space-y-6">
        {/* Stations Tab */}
        {activeTab === 'stations' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">{t('common.create')} {t('nav.stations')}</h2>
              <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
                <Input
                  placeholder={t('forms.stationIdPlaceholder')}
                  value={newStation.id}
                  onChange={(e) => setNewStation({ ...newStation, id: e.target.value.toLowerCase() })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder={t('forms.stationNamePlaceholder')}
                  value={newStation.name}
                  onChange={(e) => setNewStation({ ...newStation, name: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder={t('forms.logoTextPlaceholder')}
                  value={newStation.logoText}
                  onChange={(e) => setNewStation({ ...newStation, logoText: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  type="color"
                  value={newStation.color}
                  onChange={(e) => setNewStation({ ...newStation, color: e.target.value })}
                  className="bg-gray-800 border-gray-700 h-10"
                />
                <Button
                  onClick={() => createStationMutation.mutate(newStation)}
                  disabled={!newStation.id || !newStation.name || createStationMutation.isPending}
                  className="bg-primary text-black hover:bg-primary/80"
                >
                  <Plus className="w-4 h-4 mr-2" />
                  {t('common.create')}
                </Button>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
              <table className="w-full">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.name')}</th>
                    <th className="text-left p-4">{t('table.logo')}</th>
                    <th className="text-left p-4">{t('table.color')}</th>
                    <th className="text-left p-4">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {stationsList.map((station) => (
                    editingStation?.id === station.id ? (
                      <tr key={station.id} className="border-t border-gray-800 bg-gray-800/50">
                        <td className="p-4 font-mono">{station.id}</td>
                        <td className="p-2">
                          <Input
                            value={editingStation.name}
                            onChange={(e) => setEditingStation({ ...editingStation, name: e.target.value })}
                            className="bg-gray-700 border-gray-600"
                          />
                        </td>
                        <td className="p-2">
                          <Input
                            value={editingStation.logoText}
                            onChange={(e) => setEditingStation({ ...editingStation, logoText: e.target.value })}
                            className="bg-gray-700 border-gray-600"
                          />
                        </td>
                        <td className="p-2">
                          <Input
                            type="color"
                            value={editingStation.color}
                            onChange={(e) => setEditingStation({ ...editingStation, color: e.target.value })}
                            className="bg-gray-700 border-gray-600 h-8 w-12"
                          />
                        </td>
                        <td className="p-4 flex gap-2">
                          <Button size="sm" onClick={() => updateStationMutation.mutate(editingStation)} className="bg-primary text-black">{t('common.save')}</Button>
                          <Button size="sm" variant="ghost" onClick={() => setEditingStation(null)}>{t('common.cancel')}</Button>
                        </td>
                      </tr>
                    ) : (
                      <tr key={station.id} className="border-t border-gray-800">
                        <td className="p-4 font-mono">{station.id}</td>
                        <td className="p-4 font-bold">{station.name}</td>
                        <td className="p-4">{station.logoText}</td>
                        <td className="p-4">
                          <span className="inline-block w-6 h-6 rounded" style={{ backgroundColor: station.color }}></span>
                        </td>
                        <td className="p-4 flex gap-2">
                          <Button variant="ghost" size="sm" onClick={() => setEditingStation(station)} className="text-blue-400 hover:text-blue-300">
                            <Edit2 className="w-4 h-4" />
                          </Button>
                          <Button variant="ghost" size="sm" onClick={() => deleteStationMutation.mutate(station.id)} className="text-red-400 hover:text-red-300">
                            <Trash2 className="w-4 h-4" />
                          </Button>
                        </td>
                      </tr>
                    )
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Fuel Types Tab */}
        {activeTab === 'fueltypes' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">{t('common.create')} {t('nav.fueltypes')}</h2>
              <div className="grid grid-cols-2 md:grid-cols-6 gap-4">
                <Input
                  placeholder={t('forms.fuelIdPlaceholder')}
                  value={newFuelType.id}
                  onChange={(e) => setNewFuelType({ ...newFuelType, id: e.target.value.toLowerCase() })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder={t('forms.fuelNamePlaceholder')}
                  value={newFuelType.name}
                  onChange={(e) => setNewFuelType({ ...newFuelType, name: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <select
                  value={newFuelType.stationId}
                  onChange={(e) => setNewFuelType({ ...newFuelType, stationId: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">{t('forms.selectStation')}</option>
                  {stationsList.map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
                <Input
                  type="number"
                  placeholder={t('forms.basePricePlaceholder')}
                  value={newFuelType.basePrice || ""}
                  onChange={(e) => setNewFuelType({ ...newFuelType, basePrice: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  type="number"
                  placeholder={t('forms.discountPricePlaceholder')}
                  value={newFuelType.discountPrice || ""}
                  onChange={(e) => setNewFuelType({ ...newFuelType, discountPrice: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Button
                  onClick={() => createFuelTypeMutation.mutate(newFuelType)}
                  disabled={!newFuelType.id || !newFuelType.name || !newFuelType.stationId || createFuelTypeMutation.isPending}
                  className="bg-primary text-black hover:bg-primary/80"
                >
                  <Plus className="w-4 h-4 mr-2" />
                  {t('common.create')}
                </Button>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
              <table className="w-full">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.name')}</th>
                    <th className="text-left p-4">{t('table.station')}</th>
                    <th className="text-left p-4">{t('table.basePrice')}</th>
                    <th className="text-left p-4">{t('table.discountPrice')}</th>
                    <th className="text-left p-4">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {fuelTypesList.map((fuel) => (
                    editingFuelType?.id === fuel.id ? (
                      <tr key={fuel.id} className="border-t border-gray-800 bg-gray-800/50">
                        <td className="p-4 font-mono">{fuel.id}</td>
                        <td className="p-2">
                          <Input
                            value={editingFuelType.name}
                            onChange={(e) => setEditingFuelType({ ...editingFuelType, name: e.target.value })}
                            className="bg-gray-700 border-gray-600"
                          />
                        </td>
                        <td className="p-4">{stationsList.find(s => s.id === fuel.stationId)?.name || fuel.stationId}</td>
                        <td className="p-2">
                          <Input
                            type="number"
                            value={editingFuelType.basePrice}
                            onChange={(e) => setEditingFuelType({ ...editingFuelType, basePrice: parseInt(e.target.value) || 0 })}
                            className="bg-gray-700 border-gray-600 w-24"
                          />
                        </td>
                        <td className="p-2">
                          <Input
                            type="number"
                            value={editingFuelType.discountPrice}
                            onChange={(e) => setEditingFuelType({ ...editingFuelType, discountPrice: parseInt(e.target.value) || 0 })}
                            className="bg-gray-700 border-gray-600 w-24"
                          />
                        </td>
                        <td className="p-4 flex gap-2">
                          <Button size="sm" onClick={() => updateFuelTypeMutation.mutate(editingFuelType)} className="bg-primary text-black">{t('common.save')}</Button>
                          <Button size="sm" variant="ghost" onClick={() => setEditingFuelType(null)}>{t('common.cancel')}</Button>
                        </td>
                      </tr>
                    ) : (
                      <tr key={fuel.id} className="border-t border-gray-800">
                        <td className="p-4 font-mono">{fuel.id}</td>
                        <td className="p-4 font-bold">{fuel.name}</td>
                        <td className="p-4">{stationsList.find(s => s.id === fuel.stationId)?.name || fuel.stationId}</td>
                        <td className="p-4 text-gray-400">{fuel.basePrice} UAH</td>
                        <td className="p-4 text-primary font-bold">{fuel.discountPrice} UAH</td>
                        <td className="p-4 flex gap-2">
                          <Button variant="ghost" size="sm" onClick={() => setEditingFuelType(fuel)} className="text-blue-400 hover:text-blue-300">
                            <Edit2 className="w-4 h-4" />
                          </Button>
                          <Button variant="ghost" size="sm" onClick={() => deleteFuelTypeMutation.mutate(fuel.id)} className="text-red-400 hover:text-red-300">
                            <Trash2 className="w-4 h-4" />
                          </Button>
                        </td>
                      </tr>
                    )
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* QR Codes Tab */}
        {activeTab === 'qrcodes' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="grid grid-cols-2 gap-4 mb-6">
              <div className="bg-green-500/20 border border-green-500/30 rounded-xl p-4">
                <div className="text-3xl font-bold text-green-400">{availableQrs.length}</div>
                <div className="text-gray-400">{t('dashboard.availableQrs')}</div>
              </div>
              <div className="bg-red-500/20 border border-red-500/30 rounded-xl p-4">
                <div className="text-3xl font-bold text-red-400">{soldQrs.length}</div>
                <div className="text-gray-400">{t('dashboard.soldQrs')}</div>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">{t('common.create')} {t('nav.qrcodes')}</h2>
              <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
                <select
                  value={newQr.stationId}
                  onChange={(e) => setNewQr({ ...newQr, stationId: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">{t('forms.selectStation')}</option>
                  {stationsList.map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
                <select
                  value={newQr.fuelType}
                  onChange={(e) => setNewQr({ ...newQr, fuelType: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">{t('forms.selectFuel')}</option>
                  {fuelTypesList
                    .filter(f => !newQr.stationId || f.stationId === newQr.stationId)
                    .map((f) => (
                      <option key={f.id} value={f.id}>{f.name}</option>
                    ))}
                </select>
                <select
                  value={newQr.liters}
                  onChange={(e) => setNewQr({ ...newQr, liters: parseInt(e.target.value) })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value={10}>10 L</option>
                  <option value={20}>20 L</option>
                  <option value={50}>50 L</option>
                </select>
                <Input
                  placeholder={t('forms.qrUrlPlaceholder')}
                  value={newQr.qrCodeUrl}
                  onChange={(e) => setNewQr({ ...newQr, qrCodeUrl: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <Button
                  onClick={() => createQrMutation.mutate(newQr)}
                  disabled={!newQr.stationId || !newQr.fuelType || !newQr.qrCodeUrl || createQrMutation.isPending}
                  className="bg-primary text-black hover:bg-primary/80"
                >
                  <Plus className="w-4 h-4 mr-2" />
                  {t('common.create')}
                </Button>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
              <table className="w-full">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.station')}</th>
                    <th className="text-left p-4">{t('table.fuel')}</th>
                    <th className="text-left p-4">{t('table.liters')}</th>
                    <th className="text-left p-4">{t('table.qrPreview')}</th>
                    <th className="text-left p-4">{t('common.status')}</th>
                    <th className="text-left p-4">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {qrCodes.map((qr) => (
                    <tr key={qr.id} className="border-t border-gray-800">
                      <td className="p-4">{qr.id}</td>
                      <td className="p-4">{stationsList.find(s => s.id === qr.stationId)?.name || qr.stationId}</td>
                      <td className="p-4">{fuelTypesList.find(f => f.id === qr.fuelType)?.name || qr.fuelType}</td>
                      <td className="p-4">{qr.liters}L</td>
                      <td className="p-4">
                        {qr.qrCodeUrl.startsWith("http") ? (
                          <img src={qr.qrCodeUrl} alt="QR" className="w-10 h-10 object-cover rounded" />
                        ) : (
                          <span className="text-xs text-gray-500 truncate max-w-[100px] block">{qr.qrCodeUrl}</span>
                        )}
                      </td>
                      <td className="p-4">
                        <span className={`px-2 py-1 rounded text-xs ${qr.status === "available" ? "bg-green-500/20 text-green-400" : "bg-red-500/20 text-red-400"}`}>
                          {qr.status}
                        </span>
                      </td>
                      <td className="p-4">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => deleteQrMutation.mutate(qr.id)}
                          disabled={qr.status === "sold"}
                          className="text-red-400 hover:text-red-300"
                        >
                          <Trash2 className="w-4 h-4" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Packages Tab */}
        {activeTab === 'packages' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h2 className="text-xl font-bold mb-4">{t('common.create')} {t('nav.packages')}</h2>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <Input
                  placeholder={t('forms.packageIdPlaceholder')}
                  value={newPackage.id}
                  onChange={(e) => setNewPackage({ ...newPackage, id: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <select
                  value={newPackage.stationId}
                  onChange={(e) => setNewPackage({ ...newPackage, stationId: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">{t('forms.selectStation')}</option>
                  {stationsList.map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
                <select
                  value={newPackage.fuelTypeId}
                  onChange={(e) => {
                    const fuel = fuelTypesList.find(f => f.id === e.target.value);
                    setNewPackage({ ...newPackage, fuelTypeId: e.target.value, fuelName: fuel?.name || "" });
                  }}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">{t('forms.selectFuel')}</option>
                  {fuelTypesList
                    .filter(f => !newPackage.stationId || f.stationId === newPackage.stationId)
                    .map((f) => (
                      <option key={f.id} value={f.id}>{f.name}</option>
                    ))}
                </select>
                <select
                  value={newPackage.liters}
                  onChange={(e) => setNewPackage({ ...newPackage, liters: parseInt(e.target.value) })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value={10}>10 L</option>
                  <option value={20}>20 L</option>
                  <option value={50}>50 L</option>
                </select>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mt-4">
                <Input
                  type="number"
                  placeholder={t('forms.priceUahPlaceholder')}
                  value={newPackage.price || ""}
                  onChange={(e) => setNewPackage({ ...newPackage, price: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  type="number"
                  placeholder={t('forms.originalPriceUahPlaceholder')}
                  value={newPackage.originalPrice || ""}
                  onChange={(e) => setNewPackage({ ...newPackage, originalPrice: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Button
                  onClick={() => createPackageMutation.mutate(newPackage)}
                  disabled={!newPackage.id || !newPackage.stationId || !newPackage.price || createPackageMutation.isPending}
                  className="bg-primary text-black hover:bg-primary/80"
                >
                  <Plus className="w-4 h-4 mr-2" />
                  {t('common.create')}
                </Button>
              </div>
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
              <table className="w-full">
                <thead className="bg-gray-800">
                  <tr>
                    <th className="text-left p-4">{t('table.id')}</th>
                    <th className="text-left p-4">{t('table.station')}</th>
                    <th className="text-left p-4">{t('table.fuel')}</th>
                    <th className="text-left p-4">{t('table.liters')}</th>
                    <th className="text-left p-4">{t('table.price')}</th>
                    <th className="text-left p-4">{t('table.original')}</th>
                    <th className="text-left p-4">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {packages.map((pkg) => (
                    <tr key={pkg.id} className="border-t border-gray-800">
                      <td className="p-4 font-mono text-sm">{pkg.id}</td>
                      <td className="p-4">{stationsList.find(s => s.id === pkg.stationId)?.name || pkg.stationId}</td>
                      <td className="p-4">{pkg.fuelName}</td>
                      <td className="p-4">{pkg.liters}L</td>
                      <td className="p-4 text-primary font-bold">{pkg.price} UAH</td>
                      <td className="p-4 text-gray-500 line-through">{pkg.originalPrice} UAH</td>
                      <td className="p-4">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => deletePackageMutation.mutate(pkg.id)}
                          className="text-red-400 hover:text-red-300"
                        >
                          <Trash2 className="w-4 h-4" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Purchases Tab */}
        {activeTab === 'purchases' && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
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
                  </tr>
                </thead>
                <tbody>
                  {purchases.map((purchase) => (
                    <tr key={purchase.id} className="border-t border-gray-800">
                      <td className="p-4">{purchase.id}</td>
                      <td className="p-4">{purchase.stationName}</td>
                      <td className="p-4">{purchase.fuelName}</td>
                      <td className="p-4">{purchase.liters}L</td>
                      <td className="p-4 text-primary font-bold">{purchase.price} UAH</td>
                      <td className="p-4">
                        <span className={`px-2 py-1 rounded text-xs ${purchase.status === "delivered" ? "bg-green-500/20 text-green-400" :
                          purchase.status === "pending" ? "bg-yellow-500/20 text-yellow-400" :
                            purchase.status === "pending_qr" ? "bg-orange-500/20 text-orange-400" :
                              "bg-red-500/20 text-red-400"
                          }`}>
                          {purchase.status}
                        </span>
                      </td>
                      <td className="p-4 text-gray-400 text-sm">
                        {new Date(purchase.createdAt).toLocaleDateString()}
                      </td>
                    </tr>
                  ))}
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
                    <p className="text-sm text-gray-400">Вибрано {importFiles.length} файл(ів):</p>
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
                      setImportResult({ success: 0, errors: 0, existing: 0, modelUsed: '' }); // Reset stats
                      const formData = new FormData();
                      importFiles.forEach(file => formData.append('files', file));
                      try {
                        const res = await apiRequest("POST", "/api/vouchers/import", formData);
                        const { jobId } = await res.json();

                        // Poll for completion
                        let jobStatus = 'processing';
                        let jobData: any = null;

                        setImportProgress({ processed: 0, total: importFiles.length });

                        while (jobStatus === 'processing') {
                          await new Promise(r => setTimeout(r, 2000));
                          const pollRes = await apiRequest("GET", `/api/vouchers/import-status/${jobId}`);
                          jobData = await pollRes.json();
                          jobStatus = jobData.status;

                          setImportProgress({
                            processed: jobData.processedFiles || 0,
                            total: jobData.totalFiles || importFiles.length
                          });
                        }

                        if (jobStatus === 'completed' || jobStatus === 'completed_with_errors') {
                          setImportStatus(jobStatus === 'completed_with_errors' ? 'error' : 'completed');
                          setImportResult({
                            success: jobData.successfulFiles || 0,
                            errors: jobData.errorLog?.length || jobData.failedFiles || 0,
                            existing: jobData.duplicateVouchers || 0,
                            modelUsed: jobData.modelUsed || ''
                          });
                        } else {
                          // Job explicitly failed
                          setImportResult({
                            success: jobData?.successfulFiles || 0,
                            errors: jobData?.errorLog?.length || importFiles.length,
                            existing: jobData?.duplicateVouchers || 0,
                            modelUsed: jobData?.modelUsed || ''
                          });
                          throw new Error('Job failed');
                        }

                      } catch (e) {
                        console.error('Import failed:', e);
                        setImportStatus('error');
                        // Don't overwrite if we already set partial results above
                        setImportResult(prev => {
                          if (prev.success > 0 || prev.existing > 0) return prev;
                          return { success: 0, errors: importFiles.length, existing: 0, modelUsed: '' };
                        });
                      }
                      setIsImporting(false);
                      queryClient.invalidateQueries({ queryKey: ["/api/vouchers"] });
                      setImportFiles([]);
                    }}
                    disabled={isImporting || importFiles.length === 0}
                    className="bg-green-600 text-white hover:bg-green-700"
                  >
                    {isImporting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <FileUp className="w-4 h-4 mr-2" />}
                    {t('import.start')}
                  </Button>
                </div>
                {importFiles.length > 0 && <div className="mt-4 text-green-400">{importFiles.length} {t('import.filesSelected')}</div>}
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
                    <div className="text-xs text-red-400">{t('import.errorOccurred')}</div>
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
                    <div className="text-xs text-gray-500 uppercase tracking-wider">Total</div>
                    <div className="text-2xl font-bold text-white">{globalTotal}</div>
                  </div>
                  {filterFuelType && (
                    <div className="animate-in fade-in">
                      <div className="text-xs text-gray-500 uppercase tracking-wider">Filtered ({filterFuelType})</div>
                      <div className="text-2xl font-bold text-primary">{totalVouchers}</div>
                    </div>
                  )}
                  {selectedVoucherIds.size > 0 && (
                    <div className="animate-in fade-in">
                      <div className="text-xs text-gray-500 uppercase tracking-wider">Selected</div>
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
                          <SelectValue placeholder="Fuel Type" />
                        </div>
                      </SelectTrigger>
                      <SelectContent className="bg-gray-900 border-gray-800 text-white shadow-2xl">
                        <SelectItem value="all">All Fuel Types</SelectItem>
                        {dropdownFuelTypes.sort().map(name => (
                          <SelectItem key={name} value={name}>{name}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>

                  {selectedVoucherIds.size > 0 ? (
                    <Button variant="destructive" size="sm" onClick={() => setShowDeleteConfirm(true)}>
                      <Trash2 className="w-4 h-4 mr-2" />
                      Delete ({selectedVoucherIds.size})
                    </Button>
                  ) : (
                    vouchers.length > 0 && (
                      <Button variant="ghost" size="sm" onClick={() => bulkDeleteMutation.mutate()} className="text-red-400 hover:text-red-300 hover:bg-red-900/10">
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
                    {vouchers.map((v: any) => {
                      const qrData = v.qrCodeData || v.qr_code_data;
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
                          <td className="p-4" onClick={() => setSelectedQrData(qrData)}>
                            {qrData ? (
                              <div className="cursor-pointer hover:scale-105 transition-transform bg-white/5 p-1 rounded-md w-fit border border-gray-700">
                                <QRCodeCanvas value={qrData} size={32} />
                              </div>
                            ) : (
                              <div className="w-8 h-8 bg-gray-800/50 rounded animate-pulse" />
                            )}
                          </td>
                          <td className="p-4 font-bold text-white">{v.amount} L</td>
                          <td className="p-4">
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-800 text-gray-300 border border-gray-700">
                              {v.fuelType}
                            </span>
                          </td>
                          <td className="p-4 font-medium text-gray-300">{v.provider || "Unknown"}</td>
                          <td className="p-4 text-gray-400 font-mono text-xs">
                            {v.expirationDate ? new Date(v.expirationDate).toLocaleDateString() : '-'}
                          </td>
                          <td className="p-4 font-mono text-xs text-gray-500">{v.externalId}</td>
                          <td className="p-4">
                            <span className={`px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md ${v.status === 'available' ? 'bg-green-500/10 text-green-400 border-green-500/20' :
                              v.status === 'assigned' ? 'bg-blue-500/10 text-blue-400 border-blue-500/20' :
                                v.status === 'used' ? 'bg-gray-500/10 text-gray-400 border-gray-500/20' :
                                  v.status === 'sold' ? 'bg-orange-500/10 text-orange-400 border-orange-500/20' :
                                    'bg-yellow-500/10 text-yellow-400 border-yellow-500/20'
                              }`}>
                              {t(`status.${v.status || 'imported'}`)}
                            </span>
                          </td>
                          <td className="p-4 text-gray-500 text-xs font-mono">
                            {new Date(v.createdAt || Date.now()).toLocaleDateString()}
                          </td>

                        </tr>
                      );
                    })}
                    {vouchers.length === 0 && (
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
      </div>

      {selectedQrData && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4" onClick={() => setSelectedQrData(null)}>
          <div className="bg-white p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={e => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-black mb-4">{t('import.scanTitle')}</h3>
            <div className="w-full h-64 bg-white flex items-center justify-center mb-4 rounded-lg border-2 border-dashed border-gray-200">
              <QRCodeCanvas value={selectedQrData} size={200} />
            </div>
            <p className="font-mono text-xs break-all text-gray-500 mb-4 bg-gray-100 p-2 rounded">{selectedQrData}</p>
            <Button className="w-full font-bold" onClick={() => setSelectedQrData(null)}>{t('common.cancel')}</Button>
          </div>
        </div>
      )}

      {showDeleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4">
          <div className="bg-gray-900 border border-gray-800 p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={e => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-2">Confirm Deletion</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete <span className="text-white font-bold">{selectedVoucherIds.size}</span> vouchers?
              This action cannot be undone.
            </p>
            <div className="flex gap-2">
              <Button variant="outline" className="flex-1" onClick={() => setShowDeleteConfirm(false)}>Cancel</Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => {
                  deleteSelectedMutation.mutate(Array.from(selectedVoucherIds));
                  setShowDeleteConfirm(false);
                }}
              >
                Delete
              </Button>
            </div>
          </div>
        </div>
      )}
    </Layout>
  );
}
