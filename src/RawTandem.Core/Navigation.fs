module RawTandem.Core.Navigation

/// Index borné sur [0, itemCount - 1] ; 0 si la liste est vide.
let private clamp (itemCount: int) (index: int) =
    if itemCount <= 0 then 0
    else index |> max 0 |> min (itemCount - 1)

/// Index suivant dans une liste de taille itemCount, borné au dernier élément.
let next (itemCount: int) (index: int) : int =
    clamp itemCount (index + 1)

/// Index précédent, borné au premier élément.
let previous (itemCount: int) (index: int) : int =
    clamp itemCount (index - 1)

/// Retire les éléments vérifiant le prédicat et renvoie la liste restante,
/// avec l'index courant ajusté à la nouvelle taille.
let removeWhere (shouldRemove: 'a -> bool) (items: 'a list) (currentIndex: int) : 'a list * int =
    let remaining = items |> List.filter (not << shouldRemove)
    remaining, clamp remaining.Length currentIndex

/// Index borné sur [0, itemCount - 1] ; 0 si la liste est vide.
let bounded (itemCount: int) (index: int) : int = clamp itemCount index

/// Retire un élément par valeur et renvoie la liste restante,
/// avec l'index courant ajusté à la nouvelle taille.
let removeItem (item: 'a) (items: 'a list) (currentIndex: int) : 'a list * int =
    removeWhere (fun i -> i = item) items currentIndex
