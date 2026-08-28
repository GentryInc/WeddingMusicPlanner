namespace WeddingMusicPlannerPro.ViewModels

open System
open System.ComponentModel
open System.Windows.Input

type RelayCommand(execute: obj -> unit) =
    let canExecuteChanged = Event<EventHandler, EventArgs>()
    interface ICommand with
        [<CLIEvent>]
        member _.CanExecuteChanged = canExecuteChanged.Publish
        member _.CanExecute(_) = true
        member _.Execute(parameter) = execute parameter

type MainWindowViewModel() =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()
    let mutable currentTrackName = "No track loaded"

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish

    member private this.OnPropertyChanged(name: string) =
        propertyChanged.Trigger(this, PropertyChangedEventArgs(name))

    member this.CurrentTrackName
        with get () = currentTrackName
        and set value =
            if currentTrackName <> value then
                currentTrackName <- value
                this.OnPropertyChanged(nameof this.CurrentTrackName)

    member this.LoadTrackCommand =
        RelayCommand(fun _ -> this.CurrentTrackName <- "Track loaded (stub)") :> ICommand

    member this.PlayCommand =
        RelayCommand(fun _ -> ()) :> ICommand

    member this.PauseCommand =
        RelayCommand(fun _ -> ()) :> ICommand

    member this.EmergencyFadeCommand =
        RelayCommand(fun _ -> ()) :> ICommand
