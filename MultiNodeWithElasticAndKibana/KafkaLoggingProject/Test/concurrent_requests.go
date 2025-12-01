package main

import (
	"fmt"
	"io"
	"net/http"
	"sync"
	"time"
)

// Toplamda çalıştırmak istediğimiz Go routine sayısı
const totalRoutines = 1000

// Hedef API'nin temel adresi ve portu
const baseURL = "http://localhost:5000"

// Hedef endpoint'ler
var endpoints = []string{
	"/api/success",       // HTTP 200 (Log Topic: http-200)
	"/api/not-found",     // HTTP 404 (Log Topic: http-404)
	"/api/server-error",  // HTTP 500 (Log Topic: http-500)
}

// Yük dağılımı: 1000 istek, 334, 333, 333 şeklinde bölünecek.
const split1 = 334 // Success için
const split2 = 333 // Not Found için
//Geri kalanı 333 (Server Error için)

func main() {
	var wg sync.WaitGroup

	fmt.Printf("Ana program başladı. %d adet eş zamanlı istek 3 endpoint'e bölünüyor...\n", totalRoutines)
	fmt.Printf("Yük Dağılımı: Success (%d), Not Found (%d), Server Error (%d)\n", split1, split2, totalRoutines-split1-split2)

	wg.Add(totalRoutines)

	for i := 1; i <= totalRoutines; i++ {
		var targetURL string

		// 1000 isteği 3'e bölerek hangi endpoint'e gideceğini belirliyoruz.
		if i <= split1 {
			targetURL = baseURL + endpoints[0] // Success (200)
		} else if i <= split1+split2 {
			targetURL = baseURL + endpoints[1] // Not Found (404)
		} else {
			targetURL = baseURL + endpoints[2] // Server Error (500)
		}

		// Her döngüde yeni bir Go routine başlatıyoruz.
		go makeRequest(i, targetURL, &wg)
	}

	fmt.Println("Tüm Go routine'ler başlatıldı. Bitirmeleri bekleniyor...")
	wg.Wait()

	fmt.Println("✅ Tüm 1000 istek tamamlandı. Ana program bitiriliyor.")
}

// makeRequest gerçek HTTP isteğini yapar ve sonucu bildirir.
func makeRequest(id int, url string, wg *sync.WaitGroup) {
	defer wg.Done()

	// HTTP istemcisi oluştur
	client := http.Client{
		Timeout: 5 * time.Second, // Zaman aşımı süresi
	}

	resp, err := client.Get(url)
	if err != nil {
		fmt.Printf("❌ İstek #%d (%s) HATASI: %v\n", id, url, err)
		return
	}
	defer resp.Body.Close()

	// Cevap gövdesini tüket (bellek sızıntısını önlemek için önemlidir)
	io.Copy(io.Discard, resp.Body)

	// Sadece 50'nin katlarını loglayarak terminali temiz tutuyoruz.
	if id%50 == 0 || resp.StatusCode >= 400 {
		fmt.Printf("   -> İstek #%d tamamlandı. URL: %s, Status: %s\n", id, url, resp.Status)
	}
}