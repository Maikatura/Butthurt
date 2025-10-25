using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.ViewModels;

namespace Butthurt.Contracts.Views;

public partial class RoadmapView : UserControl, IPageView
    {
        public string Title => "Roadmap";
        public SideBarType SidebarType => SideBarType.Footer;
        public event EventHandler? BackRequested;

        public RoadmapView()
        {
            InitializeComponent();
            
            
            

            // Add sample roadmap items
            AddRoadmapItem("Design Sidebar", "Plan sidebar layout and categories", "\u270F"); // ✏
            AddRoadmapItem("Implement Dynamic Sections", "Sections are created automatically", "\u2699"); // ⚙
            AddRoadmapItem("Add Roadmap Page", "Flexible roadmap page layout", "\u1F4C5"); // 📅
            AddRoadmapItem("Add New Features", "Easily extend roadmap dynamically", "\u2B50"); // ⭐
            
            // Inside constructor after InitializeComponent()
            
            
            var observer = new AnonymousObserver<Rect>(bounds =>
            {
                if (bounds.Width <= 0) return;

                double cardWidth = 250;          // preferred width
                double spacing = RoadmapContainer.ItemSpacing;
                double availableWidth = bounds.Width;

                // Maximum 3 cards per row
                int maxColumns = Math.Min(3, Math.Max(1, (int)((availableWidth + spacing) / (cardWidth + spacing))));

                // Calculate actual width per card to fill row if less than maxColumns
                double actualCardWidth = cardWidth;

                if (maxColumns < 3)
                {
                    // Expand cards to fill available width evenly
                    actualCardWidth = (availableWidth - (maxColumns - 1) * spacing) / maxColumns;
                    actualCardWidth = Math.Min(cardWidth, actualCardWidth); // don’t exceed preferred width
                }

                foreach (var child in RoadmapContainer.Children)
                {
                    if (child is Border card)
                        card.Width = actualCardWidth;
                }

                // Optional: constrain WrapPanel width so it wraps correctly
                RoadmapContainer.Width = maxColumns * actualCardWidth + (maxColumns - 1) * spacing;
            });


            
            this.GetObservable(BoundsProperty).Subscribe(observer);
        }

        /// <summary>
        /// Dynamically adds a roadmap card.
        /// </summary>
        public void AddRoadmapItem(string title, string subtitle = "", string iconText = "")
        {
            var icon = new TextBlock
            {
                Text = iconText,
                FontSize = 24,
                Foreground = Brushes.LightBlue,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var textStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                    new TextBlock { Text = subtitle, FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap }
                }
            };

            var card = new Border
            {
                Width = 250, // preferred width
                Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { icon, textStack }
                }
            };


            // Hover effect
            card.PointerEntered += (_, _) => card.Background = new SolidColorBrush(Color.Parse("#2A2A2A"));
            card.PointerExited += (_, _) => card.Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

            RoadmapContainer.Children.Add(card);
        }

    }