namespace WeddingMusicPlannerPro

open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open WeddingMusicPlannerPro.ViewModels

type MainWindow () as this = 
    inherit Window ()

    do
        this.InitializeComponent()
        this.DataContext <- MainWindowViewModel()

    member private this.InitializeComponent() =
        AvaloniaXamlLoader.Load(this)
