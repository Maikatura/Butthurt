using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.ViewModels;

namespace Butthurt.Contracts.Views;

public class RainLayer : Control
{
    private readonly Random _rand = new();
    private readonly List<Drop> _drops = new();
    private DispatcherTimer _timer;

    private float FPS = 60;
    
    public int DropCount { get; set; } = 10;

    public RainLayer()
    {
        for (int i = 0; i < DropCount; i++)
        {
            _drops.Add(new Drop
            {
                X = _rand.NextDouble(),
                Y = _rand.NextDouble(),
                Speed = 0.002 + _rand.NextDouble() * 0.008, // 0.002–0.01

                Length = 0.05 + _rand.NextDouble() * 0.05
            });
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / FPS) };
        _timer.Tick += (_, _) => InvalidateVisual(); // triggers Render()
        _timer.Start();
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        foreach (var d in _drops)
        {
            double x = d.X * w;
            double y = d.Y * h;
            double length = d.Length * h;

            context.DrawLine(new Pen(Brushes.LightBlue, 1), new Point(x, y), new Point(x, y + length));

            // Move drop
            d.Y += d.Speed;
            if (d.Y > 1) d.Y = -d.Length;
        }
    }

    private class Drop
    {
        public double X;
        public double Y;
        public double Speed;
        public double Length;
    }
}