using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

using Microsoft.Win32;

namespace XliffTranslate
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private List<Translation> translations = new List<Translation>();
        private string originalContent;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();

            CloseButton.Click += CloseApp;
            LoadButton.Click += LoadButtonClick;
            SaveLineButton.Click += SaveLineButtonClick;
            SaveToFileButton.Click += SaveToFileButtonClick;
            NextButton.Click += NextButtonClick;
            PreviousButton.Click += PreviousButtonClick;
            FilteredTranslationSelector.SelectionChanged += FilterSelectionChanged;

            Log("Started.", LogLevel.Ok);
            Log("Version: Alpha 1.0", LogLevel.Info);

            TranslationStates.Add("new");
            TranslationStates.Add("needs-review-translation");
            TranslationStates.Add("translated");

            DataContext = this;
        }

        public bool IsTranslationsLoaded { get; set; }
        public bool CanSaveTranslations { get; set; }
        public Visibility EditorVisiblity { get; set; } = Visibility.Collapsed;
        public ObservableCollection<Translation> FilteredTranslations { get; set; } = new ObservableCollection<Translation>();
        public List<Translation> TranslationChangeRequests { get; set; } = new List<Translation>();
        public Translation SelectedItem { get; set; } = new Translation();
        public ObservableCollection<string> TranslationStates { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<LogEntry> LogEntries { get; set; } = new ObservableCollection<LogEntry>();

        private void LoadTranslations(string path = null)
        {
            CanSaveTranslations = false;
            EditorVisiblity = Visibility.Collapsed;
            IsTranslationsLoaded = false;
            translations.Clear();
            try
            {
                XmlDocument doc = LoadDocument(path);
                LoadTranslations(doc);
                IsTranslationsLoaded = true;
                Log("Translations succesfully loaded.", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                Log(ex.Message, LogLevel.Error);
            }

            SelectFirstTranslation();
        }

        private XmlDocument LoadDocument(string path = null)
        {
            if (path == null)
            {
                var fileDialog = new OpenFileDialog
                {
                    Filter = "XLIFF File|*.xlf",
                    Multiselect = false
                };

                fileDialog.ShowDialog();

                path = fileDialog.FileName;
            }

            Log($"Loading document. Path=[{path}]");

            var fileContent = File.ReadAllText(path);

            originalContent = fileContent;

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(fileContent);
            return doc;
        }

        private void LoadTranslations(XmlDocument doc)
        {
            Log($"Loading translations.");

            var groupNode = doc.DocumentElement.FirstChild.FirstChild.NextSibling.FirstChild;

            foreach (XmlElement translationNode in groupNode.ChildNodes)
            {
                CreateTranslationItem(translationNode);
            }

            FilteredTranslations = new ObservableCollection<Translation>(translations);
        }

        private void CreateTranslationItem(XmlElement translationNode)
        {
            var translationItem = new Translation()
            {
                Id = translationNode.GetAttribute("id")
            };
            foreach (object innerNode in translationNode.ChildNodes)
            {
                if (innerNode is XmlElement translationData)
                {
                    if (translationData.Name.Equals("source"))
                    {
                        translationItem.Source = translationData.InnerText;
                    }
                    if (translationData.Name.Equals("target"))
                    {
                        translationItem.Target = translationData.InnerText;
                        translationItem.State = translationData.GetAttribute("state");
                    }
                }
            }
            translations.Add(translationItem);
        }

        private void FilterTranslations()
        {
            if (SearchTextBox.Text == null || SearchTextBox.Text == String.Empty)
            {
                FilteredTranslations = new ObservableCollection<Translation>(translations);
                Log($"Translations unfiltered, new amount {FilteredTranslations.Count}.");
            }
            else
            {
                var filteredTranslations = translations.Where(t => t.Source.ToLowerInvariant().Contains(SearchTextBox.Text.ToLowerInvariant()));
                FilteredTranslations = new ObservableCollection<Translation>(filteredTranslations);
                Log($"Filtered translations, found {FilteredTranslations.Count}.");
            }

            SelectFirstTranslation();
        }

        private void SelectTranslationById(string id)
        {
            SelectedItem = FilteredTranslations.FirstOrDefault(t => t.Id.Equals(id));
        }

        private void SelectFirstTranslation()
        {
            SelectedItem = FilteredTranslations.FirstOrDefault();
        }

        private void SelectNextTranslation()
        {
            try
            {
                var currentItemIndex = FilteredTranslations.IndexOf(SelectedItem);

                if (currentItemIndex >= FilteredTranslations.Count - 1)
                {
                    SelectedItem = FilteredTranslations.FirstOrDefault();
                    return;
                }

                SelectedItem = FilteredTranslations.ElementAt(currentItemIndex + 1);
            }
            catch (Exception ex)
            {
                Log(ex.Message, LogLevel.Error);
            }
        }

        private void SelectPreviousTranslation()
        {
            try
            {
                var currentItemIndex = FilteredTranslations.IndexOf(SelectedItem);

                if (currentItemIndex <= 0)
                {
                    SelectedItem = FilteredTranslations.LastOrDefault();
                    return;
                }

                SelectedItem = FilteredTranslations.ElementAt(currentItemIndex - 1);
            }
            catch (Exception ex)
            {
                Log(ex.Message, LogLevel.Error);
            }
        }

        private void AddTranslationRequestToCollection()
        {
            var previousRequest = TranslationChangeRequests.FirstOrDefault(t => t.Id.Equals(SelectedItem.Id));
            if (previousRequest != null)
            {
                TranslationChangeRequests.Remove(previousRequest);
                Log($"Overwriting previous change. Id=[{previousRequest.Id}] Target=[{previousRequest.Target}] State=[{previousRequest.State}]", LogLevel.Warning);
            }

            var translationItemToChange = new Translation
            {
                Id = (string)SelectedItem.Id.Clone(),
                Source = (string)SelectedItem.Source.Clone(),
                Target = (string)SelectedItem.Target.Clone(),
                State = (string)SelectedItem.State.Clone()
            };

            TranslationChangeRequests.Add(translationItemToChange);

            CanSaveTranslations = true;

            Log($"Saved. Id=[{translationItemToChange.Id}]", LogLevel.Ok);
        }

        private void SaveChangesToFile()
        {
            var fileDialog = new SaveFileDialog
            {
                Filter = "XLIFF File|*.xlf"
            };

            fileDialog.ShowDialog();

            var path = fileDialog.FileName;

            if (path == null || path.Equals(String.Empty))
            {
                Log($"No file selected, aborting save. Path=[{path}]", LogLevel.Error);
                return;
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(originalContent);

            ReplaceTranslations(doc);

            //File.Copy(path, path.Replace(".xlf", "_backup.xlf"), true);

            //Log($"Created backup. Path=[{path.Replace(".xlf", "_backup.xlf")}]");

            doc.Save(path);

            Log($"Saved modified document. Path=[{path}]", LogLevel.Ok);

            var selectedItemId = SelectedItem.Id;

            LoadTranslations(path);
            FilterTranslations();
            SelectTranslationById(selectedItemId);
        }

        private void ReplaceTranslations(XmlDocument doc)
        {
            var groupNode = doc.DocumentElement.FirstChild.FirstChild.NextSibling.FirstChild;

            foreach (XmlElement translationNode in groupNode.ChildNodes)
            {
                var translationChangeRequest = TranslationChangeRequests.FirstOrDefault(t => t.Id.Equals(translationNode.GetAttribute("id")));
                if (translationChangeRequest != null)
                {
                    foreach (var innerNode in translationNode)
                    {
                        if (innerNode is XmlElement translationData)
                        {
                            if (translationData.Name.Equals("target"))
                            {
                                translationData.InnerText = translationChangeRequest.Target;
                                translationData.SetAttribute("state", translationChangeRequest.State);
                            }
                        }
                    }
                    TranslationChangeRequests.Remove(translationChangeRequest);
                }
            }
        }

        #region Events
        private void CloseApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MoveScreen(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void LoadButtonClick(object sender, RoutedEventArgs e)
        {
            Task.Run(() => LoadTranslations());
        }

        private void NextButtonClick(object sender, RoutedEventArgs e)
        {
            SelectNextTranslation();
        }

        private void PreviousButtonClick(object sender, RoutedEventArgs e)
        {
            SelectPreviousTranslation();
        }

        private void SearchTextBoxEnter(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FilterTranslations();
            }
        }

        private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = sender as ComboBox;
            SelectedItem = s.SelectedItem as Translation;
            EditorVisiblity = Visibility.Visible;
        }

        private void SaveLineButtonClick(object sender, RoutedEventArgs e)
        {
            AddTranslationRequestToCollection();
        }

        private void SaveToFileButtonClick(object sender, RoutedEventArgs e)
        {
            SaveChangesToFile();
        }
        #endregion Events

        #region Utility
        private void Log(string message, LogLevel level = 0)
        {
            Application.Current.Dispatcher.Invoke(new Action(() => { LogEntries.Add(new LogEntry(message, level)); }));
        }
        #endregion Utility
    }

    public class Translation
    {
        public string Id { get; set; }

        public string Source { get; set; }

        public string Target { get; set; }

        public string State { get; set; }
    }

    public class LogEntry
    {
        public LogEntry(string message, LogLevel level = 0)
        {
            Message = message;

            switch (level)
            {
                case LogLevel.Info:
                    ColorBrush = new SolidColorBrush(Color.FromRgb(3, 169, 244));
                    break;
                case LogLevel.Ok:
                    ColorBrush = new SolidColorBrush(Color.FromRgb(139, 195, 74));
                    break;
                case LogLevel.Warning:
                    ColorBrush = new SolidColorBrush(Color.FromRgb(255, 235, 59));
                    break;
                case LogLevel.Error:
                    ColorBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    break;
                default:
                    break;
            }
        }

        public SolidColorBrush ColorBrush { get; set; }
        public string Message { get; set; }
    }

    public enum LogLevel
    {
        Info,
        Ok,
        Warning,
        Error
    }
}
