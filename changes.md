# Rejestr Zmian - Drone Delivery Linux

## Aktualizacja Interfejsu (UI)

- Zmieniono architekturę z zakładek (Tabs) na nawigację stron (Start -> Mapa/Nadanie).
- Dodano nowoczesny ekran startowy z dużymi przyciskami wyboru.
- Usunięto zbędny przycisk "Odbierz paczkę".
- Wprowadzono "Premium UI":
  - Ikony wektorowe (SVG paths) zamiast emoji.
  - Cienie (BoxShadow) pod kartami i przyciskami.
  - Spójną paletę kolorów (.NET Purple, Gold, Light Gray).
  - Lepszy kontrast formularzy i przycisków.
- Mapa wyświetla teraz trasę przesyłki (niebieska linia) oraz ikonę drona.
- Lista przesyłek na mapie została uproszczona (jedna lista "Paczki") i ujednolicona wizualnie.

## Logika Biznesowa (ViewModel)

- **Walidacja Dat:** Zablokowano możliwość nadania paczki z datą dostawy wcześniejszą niż data nadania.
- **Kalkulator Kosztów:** Wprowadzono dynamiczne skalowanie ceny w zależności od pilności:
  - Przesyłka tego samego dnia: +40 PLN (najdrożej).
  - Każdy kolejny dzień zwłoki obniża cenę o 5 PLN.
  - Powyżej 8 dni dopłata za czas wynosi 0 PLN.
  - Bazowy koszt (10 PLN) i koszt wagi (2 PLN/kg) pozostają bez zmian.
- **Odświeżanie Mapy:** Poprawiono logikę wyświetlania markerów, korzystając ze wspólnej kolekcji `AllOrders`.

## Techniczne

- Wymuszono jasny motyw aplikacji (`Light Theme`) w `App.axaml` dla spójności na Linuxie.
- Rozwiązano problem znikających przycisków przy hoverze (custom `Border` controls).
- Dodano `UpdateSourceTrigger.PropertyChanged` do pól tekstowych dla natychmiastowej walidacji.
- Ikony mapy (pinezki i dron) przełączone na SVG z `Assets` (URI `file://`).
- Ustalono skale i offsety ikon mapy (lepsze dopasowanie do punktu).

