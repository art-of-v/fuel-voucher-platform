
import { useEffect, useState } from "react";
import { MapContainer, TileLayer, Marker, Popup, useMap } from "react-leaflet";
import "leaflet/dist/leaflet.css";
import L from "leaflet";
import { useQuery } from "@tanstack/react-query";
import { Link } from "wouter";
import { Navigation, MapPin, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

// Fix Leaflet marker icons
import markerIcon2x from "leaflet/dist/images/marker-icon-2x.png";
import markerIcon from "leaflet/dist/images/marker-icon.png";
import markerShadow from "leaflet/dist/images/marker-shadow.png";

delete (L.Icon.Default.prototype as any)._getIconUrl;
L.Icon.Default.mergeOptions({
    iconUrl: markerIcon,
    iconRetinaUrl: markerIcon2x,
    shadowUrl: markerShadow,
});

type Station = {
    id: string;
    name: string;
    color: string;
    logoText: string;
    lat?: string;
    lng?: string;
};

function MapUpdater({ center }: { center: [number, number] }) {
    const map = useMap();
    useEffect(() => {
        map.setView(center, 13);
    }, [center, map]);
    return null;
}

export default function StationsMap() {
    const [search, setSearch] = useState("");
    const [center, setCenter] = useState<[number, number]>([50.4501, 30.5234]); // Kyiv default

    const { data: stations = [] } = useQuery<Station[]>({
        queryKey: ["/api/stations"],
    });

    const filteredStations = stations.filter(
        (s) =>
            s.lat &&
            s.lng &&
            (s.name.toLowerCase().includes(search.toLowerCase()) ||
                s.logoText.toLowerCase().includes(search.toLowerCase()))
    );

    return (
        <div className="relative h-[calc(100vh-80px)] w-full overflow-hidden rounded-3xl border border-white/10 shadow-2xl mt-6">
            <div className="absolute top-4 left-4 right-4 z-[1000] flex gap-2">
                <div className="relative flex-1">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
                    <Input
                        placeholder="Search stations..."
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="pl-9 bg-black/80 backdrop-blur-xl border-white/10 text-white h-12 rounded-xl shadow-lg"
                    />
                </div>
                <Button
                    className="h-12 w-12 rounded-xl bg-primary text-black shadow-lg"
                    onClick={() => {
                        navigator.geolocation.getCurrentPosition((pos) => {
                            setCenter([pos.coords.latitude, pos.coords.longitude]);
                        });
                    }}
                >
                    <Navigation className="h-5 w-5" />
                </Button>
            </div>

            <MapContainer
                center={center}
                zoom={13}
                style={{ height: "100%", width: "100%", background: "#1a1a1a" }}
                className="z-0"
            >
                <TileLayer
                    attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                    url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
                />
                <MapUpdater center={center} />

                {filteredStations.map((station) => (
                    <Marker
                        key={station.id}
                        position={[parseFloat(station.lat!), parseFloat(station.lng!)]}
                    >
                        <Popup className="custom-popup">
                            <div className="p-2 min-w-[200px]">
                                <div
                                    className="h-8 w-8 rounded-full flex items-center justify-center font-bold text-xs mb-2"
                                    style={{ backgroundColor: station.color, color: "#000" }}
                                >
                                    {station.logoText}
                                </div>
                                <h3 className="font-bold text-lg">{station.name}</h3>
                                <Link href={`/station/${station.id}`}>
                                    <Button className="w-full mt-3 bg-primary text-black hover:bg-primary/90 h-8 text-xs font-bold uppercase tracking-wider">
                                        View Fuel Prices
                                    </Button>
                                </Link>
                            </div>
                        </Popup>
                    </Marker>
                ))}
            </MapContainer>
        </div>
    );
}
