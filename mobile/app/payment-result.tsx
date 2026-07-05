import { useEffect } from "react";
import { useRouter } from "expo-router";

export default function PaymentResultScreen() {
    const router = useRouter();

    useEffect(() => {
        router.replace("/my-codes");
    }, []);

    return null;
}
