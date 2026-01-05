import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Trash2, Package, QrCode, ShoppingCart, Plus, Building, Fuel, Edit2, FileUp, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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
  const [importResult, setImportResult] = useState({ success: 0, errors: 0, existing: 0 });
  const [importProgress, setImportProgress] = useState({ processed: 0, total: 0 });
  const [selectedQrData, setSelectedQrData] = useState<string | null>(null);

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

  const { data: vouchersResponse } = useQuery<any>({
    queryKey: ["/api/vouchers"],
  });

  const vouchers = vouchersResponse?.data || [];

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

  const deleteVoucherMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/vouchers/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/vouchers"] });
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
                  placeholder="Station ID (e.g. okko)"
                  value={newStation.id}
                  onChange={(e) => setNewStation({ ...newStation, id: e.target.value.toLowerCase() })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder="Station Name (e.g. OKKO)"
                  value={newStation.name}
                  onChange={(e) => setNewStation({ ...newStation, name: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder="Logo Text"
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
                    <th className="text-left p-4">ID</th>
                    <th className="text-left p-4">Name</th>
                    <th className="text-left p-4">Logo</th>
                    <th className="text-left p-4">Color</th>
                    <th className="text-left p-4">Actions</th>
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
                  placeholder="Fuel ID (e.g. a95)"
                  value={newFuelType.id}
                  onChange={(e) => setNewFuelType({ ...newFuelType, id: e.target.value.toLowerCase() })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  placeholder="Name (e.g. A-95)"
                  value={newFuelType.name}
                  onChange={(e) => setNewFuelType({ ...newFuelType, name: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <select
                  value={newFuelType.stationId}
                  onChange={(e) => setNewFuelType({ ...newFuelType, stationId: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">Select Station</option>
                  {stationsList.map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
                <Input
                  type="number"
                  placeholder="Base Price"
                  value={newFuelType.basePrice || ""}
                  onChange={(e) => setNewFuelType({ ...newFuelType, basePrice: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  type="number"
                  placeholder="Discount Price"
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
                    <th className="text-left p-4">ID</th>
                    <th className="text-left p-4">Name</th>
                    <th className="text-left p-4">Station</th>
                    <th className="text-left p-4">Base Price</th>
                    <th className="text-left p-4">Discount Price</th>
                    <th className="text-left p-4">Actions</th>
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
                <div className="text-gray-400">Available QRs</div>
              </div>
              <div className="bg-red-500/20 border border-red-500/30 rounded-xl p-4">
                <div className="text-3xl font-bold text-red-400">{soldQrs.length}</div>
                <div className="text-gray-400">Sold QRs</div>
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
                  <option value="">Select Station</option>
                  {stationsList.map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
                <select
                  value={newQr.fuelType}
                  onChange={(e) => setNewQr({ ...newQr, fuelType: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">Select Fuel</option>
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
                  placeholder="QR Code URL or Image URL"
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
                    <th className="text-left p-4">ID</th>
                    <th className="text-left p-4">Station</th>
                    <th className="text-left p-4">Fuel</th>
                    <th className="text-left p-4">Liters</th>
                    <th className="text-left p-4">QR Preview</th>
                    <th className="text-left p-4">Status</th>
                    <th className="text-left p-4">Actions</th>
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
                  placeholder="Package ID (e.g. okko_a95_10)"
                  value={newPackage.id}
                  onChange={(e) => setNewPackage({ ...newPackage, id: e.target.value })}
                  className="bg-gray-800 border-gray-700"
                />
                <select
                  value={newPackage.stationId}
                  onChange={(e) => setNewPackage({ ...newPackage, stationId: e.target.value })}
                  className="bg-gray-800 border border-gray-700 rounded-lg p-2 text-white"
                >
                  <option value="">Select Station</option>
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
                  <option value="">Select Fuel</option>
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
                  placeholder="Price (UAH)"
                  value={newPackage.price || ""}
                  onChange={(e) => setNewPackage({ ...newPackage, price: parseInt(e.target.value) || 0 })}
                  className="bg-gray-800 border-gray-700"
                />
                <Input
                  type="number"
                  placeholder="Original Price (UAH)"
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
                    <th className="text-left p-4">ID</th>
                    <th className="text-left p-4">Station</th>
                    <th className="text-left p-4">Fuel</th>
                    <th className="text-left p-4">Liters</th>
                    <th className="text-left p-4">Price</th>
                    <th className="text-left p-4">Original</th>
                    <th className="text-left p-4">Actions</th>
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
                    <th className="text-left p-4">ID</th>
                    <th className="text-left p-4">Station</th>
                    <th className="text-left p-4">Fuel</th>
                    <th className="text-left p-4">Liters</th>
                    <th className="text-left p-4">Price</th>
                    <th className="text-left p-4">Status</th>
                    <th className="text-left p-4">Date</th>
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
              <div className="flex flex-col items-center justify-center border-2 border-dashed border-gray-700 rounded-lg p-12 hover:bg-gray-800/50 transition-colors">
                <FileUp className="w-12 h-12 text-gray-500 mb-4" />
                <h3 className="text-xl font-bold mb-2">{t('import.title')}</h3>
                <p className="text-gray-400 mb-6 text-center max-w-md">{t('import.description')}</p>
                <div className="flex gap-4">
                  <Input type="file" multiple className="hidden" id="file-upload" onChange={(e) => e.target.files && setImportFiles(Array.from(e.target.files))} />
                  <Button variant="outline" onClick={() => document.getElementById('file-upload')?.click()}>{t('import.clickSelect')}</Button>
                  <Button
                    onClick={async () => {
                      if (importFiles.length === 0) return;
                      setIsImporting(true);
                      setImportStatus('processing');
                      setImportResult({ success: 0, errors: 0, existing: 0 }); // Reset stats
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

                        if (jobStatus === 'completed') {
                          setImportStatus('completed');
                          setImportResult({
                            success: jobData.successfulFiles || 0,
                            errors: jobData.failedFiles || 0,
                            existing: jobData.duplicateVouchers || 0
                          });
                        } else {
                          throw new Error('Job failed');
                        }

                      } catch (e) {
                        console.error('Import failed:', e);
                        setImportStatus('error');
                        setImportResult({ success: 0, errors: importFiles.length, existing: 0 });
                      }
                      setIsImporting(false);
                      queryClient.invalidateQueries({ queryKey: ["/api/vouchers"] });
                      setImportFiles([]);
                    }}
                    disabled={isImporting || importFiles.length === 0}
                    className="bg-green-600 text-white hover:bg-green-700"
                  >
                    {isImporting ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <FileUp className="w-4 h-4 mr-2" />}
                    Start Import
                  </Button>
                </div>
                {importFiles.length > 0 && <div className="mt-4 text-green-400">{importFiles.length} files selected</div>}
              </div>
            </div>

            {importStatus !== 'idle' && (
              <div className={`border rounded-xl p-4 mb-6 ${importStatus === 'error' ? 'bg-red-900/10 border-red-900/30' : 'bg-gray-900 border-gray-800'}`}>
                <div className="flex justify-between text-sm mb-2">
                  <span className="text-gray-400 font-bold">Статус:
                    <span className={
                      importStatus === 'completed' ? "text-green-400 uppercase ml-2" :
                        importStatus === 'error' ? "text-red-400 uppercase ml-2" :
                          "text-blue-400 animate-pulse uppercase ml-2"
                    }>
                      {importStatus === 'completed' ? 'ЗАВЕРШЕНО' : importStatus === 'error' ? 'ПОМИЛКА' : 'ОБРОБКА'}
                    </span>
                  </span>
                  <span className="text-gray-400">
                    {importStatus === 'error' ? 'Помилка імпорту' : `${importProgress.processed} з ${importProgress.total} файлів оброблено`}
                  </span>
                  {(importStatus === 'completed' || importStatus === 'error') && (
                    <button onClick={() => setImportStatus('idle')} className="text-xs text-gray-500 hover:text-white underline ml-2">Закрити</button>
                  )}
                </div>
                <div className="w-full bg-gray-800 rounded-full h-2.5 mb-2 overflow-hidden">
                  <div
                    className={`h-2.5 rounded-full transition-all duration-500 ${importStatus === 'completed' ? 'bg-green-500' :
                      importStatus === 'error' ? 'bg-red-500' :
                        'bg-blue-500 animate-pulse'
                      }`}
                    style={{ width: `${importProgress.total > 0 ? (importProgress.processed / importProgress.total) * 100 : 0}%` }}
                  ></div>
                </div>
                {importStatus === 'error' ? (
                  <div className="text-xs text-red-400">Сталася помилка під час імпорту. Перевірте логи або спробуйте ще раз.</div>
                ) : (
                  <div className="flex justify-between text-xs text-gray-500">
                    <span className="text-green-500">Успішно: {importResult.success}</span>
                    <span className="text-red-500">Помилки: {importResult.errors}</span>
                    <span className="text-orange-500">Дублікати: {importResult.existing}</span>
                  </div>
                )}
              </div>
            )}

            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">Імпорт талонів</h2>
              {vouchers.length > 0 && (
                <Button variant="destructive" size="sm" onClick={() => bulkDeleteMutation.mutate()}>Видалити усі ({vouchers.length})</Button>
              )}
            </div>

            <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-gray-800/50 text-gray-400 uppercase text-xs">
                  <tr>
                    <th className="text-left p-4">Зображення</th>
                    <th className="text-left p-4">Об'єм</th>
                    <th className="text-left p-4">Тип пального</th>
                    <th className="text-left p-4">Провайдер</th>
                    <th className="text-left p-4">Термін дії</th>
                    <th className="text-left p-4">Зовнішній ID</th>
                    <th className="text-left p-4">Статус</th>
                    <th className="text-left p-4">Дата</th>
                    <th className="text-right p-4">Дії</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800">
                  {vouchers.map((v: any) => {
                    const qrData = v.qrCodeData || v.qr_code_data;
                    return (
                      <tr key={v.id} className="hover:bg-gray-800/30 transition-colors">
                        <td className="p-4" onClick={() => setSelectedQrData(qrData)}>
                          {qrData ? (
                            <div className="cursor-pointer hover:opacity-80 bg-white p-1 rounded w-fit">
                              <QRCodeCanvas value={qrData} size={32} />
                            </div>
                          ) : (
                            <div className="w-8 h-8 bg-gray-800 rounded animate-pulse" />
                          )}
                        </td>
                        <td className="p-4 font-bold">{v.amount} liters</td>
                        <td className="p-4 text-green-400 font-medium">{v.fuelType}</td>
                        <td className="p-4 font-medium">{v.provider || "Unknown"}</td>
                        <td className="p-4 text-gray-400">
                          {v.expirationDate ? new Date(v.expirationDate).toLocaleDateString() : '-'}
                        </td>
                        <td className="p-4 font-mono text-xs text-gray-500">{v.externalId}</td>
                        <td className="p-4">
                          <span className="px-2 py-1 rounded-full text-xs font-bold bg-green-500/10 text-green-500 border border-green-500/20 uppercase">
                            AVAILABLE
                          </span>
                        </td>
                        <td className="p-4 text-gray-500 text-xs">
                          {new Date(v.createdAt || Date.now()).toLocaleDateString()}
                        </td>
                        <td className="p-4 text-right">
                          <Button variant="ghost" size="icon" onClick={() => deleteVoucherMutation.mutate(v.id)} className="text-gray-500 hover:text-red-400">
                            <Trash2 className="w-4 h-4" />
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
                  {vouchers.length === 0 && (
                    <tr><td colSpan={9} className="p-12 text-center text-gray-500">No vouchers imported yet.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {selectedQrData && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4" onClick={() => setSelectedQrData(null)}>
          <div className="bg-white p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200" onClick={e => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-black mb-4">Scan Voucher</h3>
            <div className="w-full h-64 bg-white flex items-center justify-center mb-4 rounded-lg border-2 border-dashed border-gray-200">
              <QRCodeCanvas value={selectedQrData} size={200} />
            </div>
            <p className="font-mono text-xs break-all text-gray-500 mb-4 bg-gray-100 p-2 rounded">{selectedQrData}</p>
            <Button className="w-full font-bold" onClick={() => setSelectedQrData(null)}>{t('common.cancel')}</Button>
          </div>
        </div>
      )}
    </Layout>
  );
}
