﻿/*
Copyright (c) 2014, Kevin Pope
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.PlatformAbstract;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.PrintLibrary.Provider;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.VectorMath;
using System;
using System.IO;

namespace MatterHackers.MatterControl.PrintLibrary
{
	public class LibraryDataView : ScrollableWidget
	{
		public SelectedListItems<LibraryRowItem> SelectedItems = new SelectedListItems<LibraryRowItem>();

		protected FlowLayoutWidget topToBottomItemList;

		private static LibraryProvider currentLibraryProvider;

		private RGBA_Bytes baseColor = new RGBA_Bytes(255, 255, 255);

		private bool editMode = false;

		private RGBA_Bytes hoverColor = new RGBA_Bytes(204, 204, 204, 255);

		private int hoverIndex = -1;

		private RGBA_Bytes selectedColor = new RGBA_Bytes(180, 180, 180, 255);

		private int selectedIndex = -1;

		private bool settingLocalBounds = false;

		public static RootedObjectEventHandler ChangedCurrentLibraryProvider = new RootedObjectEventHandler();

		private static LibraryDataView libraryDataViewInstance = null;

		public LibraryDataView()
		{
			currentLibraryProvider = LibraryProviderSelector.Instance;
			currentLibraryProvider.DataReloaded += LibraryDataReloaded;

			if (libraryDataViewInstance != null)
			{
				throw new Exception("There should only ever be one of these, Lars.");
			}
			libraryDataViewInstance = this;

			// set the display attributes
			{
				this.AnchorAll();
				this.BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor;
				this.ScrollArea.Padding = new BorderDouble(3, 3, 5, 3);
			}

			ScrollArea.HAnchor = HAnchor.ParentLeftRight;

			AutoScroll = true;
			topToBottomItemList = new FlowLayoutWidget(FlowDirection.TopToBottom);
			topToBottomItemList.HAnchor = HAnchor.ParentLeftRight;
			AddChild(topToBottomItemList);

			AddAllItems();

			this.MouseLeaveBounds += new EventHandler(control_MouseLeaveBounds);
		}

		public delegate void HoverValueChangedEventHandler(object sender, EventArgs e);

		public event HoverValueChangedEventHandler HoverValueChanged;

		public event Action<object, EventArgs> SelectedIndexChanged;

		private event EventHandler unregisterEvents;

		public static LibraryProvider CurrentLibraryProvider
		{
			get
			{
				return currentLibraryProvider;
			}

			set
			{
				if (currentLibraryProvider != value)
				{
					// unhook the update we were getting
					currentLibraryProvider.DataReloaded -= libraryDataViewInstance.LibraryDataReloaded;
					// and hook the new one
					value.DataReloaded += libraryDataViewInstance.LibraryDataReloaded;

					bool isChildOfCurrent = value.ParentLibraryProvider == currentLibraryProvider;

					// Dispose of all children below this one.
					while (!isChildOfCurrent && currentLibraryProvider != value
						&& currentLibraryProvider.ParentLibraryProvider != null)
					{
						LibraryProvider parent = currentLibraryProvider.ParentLibraryProvider;
						currentLibraryProvider.Dispose();
						currentLibraryProvider = parent;
					}

					currentLibraryProvider = value;

					ChangedCurrentLibraryProvider.CallEvents(null, null);
				}
			}
		}

		public bool EditMode
		{
			get { return editMode; }
			set
			{
				if (this.editMode != value)
				{
					this.editMode = value;
					if (this.editMode == false)
					{
						this.ClearSelectedItems();
					}
				}
			}
		}

		public int HoverIndex
		{
			get
			{
				return hoverIndex;
			}
			set
			{
				if (value < -1 || value >= topToBottomItemList.Children.Count)
				{
					throw new ArgumentOutOfRangeException();
				}

				if (value != hoverIndex)
				{
					hoverIndex = value;
					OnHoverIndexChanged();

					for (int index = 0; index < topToBottomItemList.Children.Count; index++)
					{
						GuiWidget child = topToBottomItemList.Children[index];
						if (index == HoverIndex)
						{
							((LibraryRowItem)child.Children[0]).IsHoverItem = true;
						}
						else if (((LibraryRowItem)child.Children[0]).IsHoverItem == true)
						{
							((LibraryRowItem)child.Children[0]).IsHoverItem = false;
						}
						child.Invalidate();
					}

					Invalidate();
				}
			}
		}

		public override RectangleDouble LocalBounds
		{
			set
			{
				if (!settingLocalBounds)
				{
					Vector2 currentTopLeftOffset = new Vector2();
					if (Parent != null)
					{
						currentTopLeftOffset = TopLeftOffset;
					}
					settingLocalBounds = true;
					if (topToBottomItemList != null)
					{
						if (VerticalScrollBar.Visible)
						{
							topToBottomItemList.Width = Math.Max(0, value.Width - ScrollArea.Padding.Width - topToBottomItemList.Margin.Width - VerticalScrollBar.Width);
						}
						else
						{
							topToBottomItemList.Width = Math.Max(0, value.Width - ScrollArea.Padding.Width - topToBottomItemList.Margin.Width);
						}
					}

					base.LocalBounds = value;
					if (Parent != null)
					{
						TopLeftOffset = currentTopLeftOffset;
					}
					settingLocalBounds = false;
				}
			}
		}

		public int SelectedIndex
		{
			get
			{
				return selectedIndex;
			}
			set
			{
				if (value < -1 || value >= topToBottomItemList.Children.Count)
				{
					throw new ArgumentOutOfRangeException();
				}
				selectedIndex = value;
				OnSelectedIndexChanged();
			}
		}

		public GuiWidget SelectedItem
		{
			get
			{
				if (SelectedIndex != -1)
				{
					return Children[SelectedIndex];
				}

				return null;
			}

			set
			{
				for (int i = 0; i < Children.Count; i++)
				{
					if (Children[SelectedIndex] == value)
					{
						SelectedIndex = i;
					}
				}
			}
		}

		public PrintItemWrapper SelectedPart
		{
			get
			{
				if (SelectedIndex >= 0)
				{
					return LibraryDataView.CurrentLibraryProvider.GetPrintItemWrapperAsync(SelectedIndex).Result;
				}
				else
				{
					return null;
				}
			}
		}

		private int Count
		{
			get
			{
				return topToBottomItemList.Children.Count;
			}
		}

		public void AddListItemToTopToBottom(GuiWidget child, int indexInChildrenList = -1)
		{
			FlowLayoutWidget itemHolder = new FlowLayoutWidget();
			itemHolder.Name = "list item holder";
			itemHolder.Margin = new BorderDouble(0, 0, 0, 0);
			itemHolder.HAnchor = Agg.UI.HAnchor.Max_FitToChildren_ParentWidth;
			itemHolder.AddChild(child);
			itemHolder.VAnchor = VAnchor.FitToChildren;
			topToBottomItemList.AddChild(itemHolder, indexInChildrenList);

			itemHolder.MouseEnterBounds += new EventHandler(itemToAdd_MouseEnterBounds);
			itemHolder.MouseLeaveBounds += new EventHandler(itemToAdd_MouseLeaveBounds);
			itemHolder.ParentChanged += new EventHandler(itemHolder_ParentChanged);
		}

		public void ClearSelected()
		{
			if (selectedIndex != -1)
			{
				selectedIndex = -1;
				OnSelectedIndexChanged();
			}
		}

		public void ClearSelectedItems()
		{
			foreach (LibraryRowItem item in SelectedItems)
			{
				item.isSelectedItem = false;
				item.selectionCheckBox.Checked = false;
			}
			this.SelectedItems.Clear();
		}

		public override void OnClosed(EventArgs e)
		{
			currentLibraryProvider.DataReloaded -= LibraryDataReloaded;

			if (unregisterEvents != null)
			{
				unregisterEvents(this, null);
			}
			base.OnClosed(e);
			libraryDataViewInstance = null;
		}

		public override void OnDraw(Graphics2D graphics2D)
		{
			//activeView.OnDraw(graphics2D);

			base.OnDraw(graphics2D);
		}

		public void OnHoverIndexChanged()
		{
			Invalidate();
			if (HoverValueChanged != null)
			{
				HoverValueChanged(this, null);
			}
		}

		public override void OnMouseDown(MouseEventArgs mouseEvent)
		{
			base.OnMouseDown(mouseEvent);
		}

		public override void OnMouseMove(MouseEventArgs mouseEvent)
		{
			base.OnMouseMove(mouseEvent);
		}

		public override void OnMouseUp(MouseEventArgs mouseEvent)
		{
			base.OnMouseUp(mouseEvent);
		}

		public void OnSelectedIndexChanged()
		{
			Invalidate();
			if (SelectedIndexChanged != null)
			{
				SelectedIndexChanged(this, null);
			}
		}

		public void RebuildView()
		{
			AddAllItems();
		}

		public override void RemoveChild(int index)
		{
			topToBottomItemList.RemoveChild(index);
		}

		public override void RemoveChild(GuiWidget childToRemove)
		{
			for (int i = topToBottomItemList.Children.Count - 1; i >= 0; i--)
			{
				GuiWidget itemHolder = topToBottomItemList.Children[i];
				if (itemHolder == childToRemove || itemHolder.Children[0] == childToRemove)
				{
					topToBottomItemList.RemoveChild(itemHolder);
				}
			}
		}

		public void RemoveSelectedIndex()
		{
			if (SelectedIndex >= 0 && SelectedIndex < Count)
			{
				RemoveChild(SelectedIndex);
			}
		}

		public void RemoveSelectedItems()
		{
			foreach (LibraryRowItem item in SelectedItems)
			{
				throw new NotImplementedException();
				//item.RemoveFromParentCollection();
			}
		}

		protected GuiWidget GetThumbnailWidget(LibraryProvider parentProvider, PrintItemCollection printItemCollection, ImageBuffer imageBuffer)
		{
			Vector2 expectedSize = new Vector2((int)(50 * TextWidget.GlobalPointSizeScaleRatio), (int)(50 * TextWidget.GlobalPointSizeScaleRatio));
			if (imageBuffer.Width != expectedSize.x)
			{
				ImageBuffer scaledImageBuffer = new ImageBuffer((int)expectedSize.x, (int)expectedSize.y, 32, new BlenderBGRA());
				scaledImageBuffer.NewGraphics2D().Render(imageBuffer, 0, 0, scaledImageBuffer.Width, scaledImageBuffer.Height);
				imageBuffer = scaledImageBuffer;
			}

			ImageWidget folderThumbnail = new ImageWidget(imageBuffer);
			folderThumbnail.BackgroundColor = ActiveTheme.Instance.PrimaryAccentColor;

			Button clickThumbnail = new Button(0, 0, folderThumbnail);
			clickThumbnail.Cursor = Cursors.Hand;

			clickThumbnail.Click += (sender, e) =>
			{
				if (parentProvider == null)
				{
					LibraryDataView.CurrentLibraryProvider = LibraryDataView.CurrentLibraryProvider.GetProviderForCollection(printItemCollection);
				}
				else
				{
					LibraryDataView.CurrentLibraryProvider = parentProvider;
				}

				UiThread.RunOnIdle(RebuildView);
			};

			return clickThumbnail;
		}

		private void AddAllItems()
		{
			topToBottomItemList.RemoveAllChildren();

			var provider = LibraryDataView.CurrentLibraryProvider;

			if (provider != null && provider.ProviderKey != "ProviderSelectorKey")
			{
				PrintItemCollection parent = new PrintItemCollection("..", provider.ProviderKey);
				LibraryRowItem queueItem = new LibraryRowItemCollection(parent, -1, this, provider.ParentLibraryProvider, GetThumbnailWidget(provider.ParentLibraryProvider, parent, LibraryProvider.UpFolderImage));
				AddListItemToTopToBottom(queueItem);
			}

			for (int i = 0; i < provider.CollectionCount; i++)
			{
				PrintItemCollection item = provider.GetCollectionItem(i);
				LibraryRowItem queueItem = new LibraryRowItemCollection(item, i, this, null, GetThumbnailWidget(null, item, provider.GetCollectionFolderImage(i)));
				AddListItemToTopToBottom(queueItem);
			}

			for (int i = 0; i < provider.ItemCount; i++)
			{
				GuiWidget thumbnailWidget = provider.GetItemThumbnail(i);
				LibraryRowItem queueItem = new LibraryRowItemPart(provider, i, this, thumbnailWidget);

				AddListItemToTopToBottom(queueItem);
			}
		}

		private void control_MouseLeaveBounds(object sender, EventArgs e)
		{
			HoverIndex = -1;
		}

		private void itemHolder_ParentChanged(object sender, EventArgs e)
		{
			FlowLayoutWidget itemHolder = (FlowLayoutWidget)sender;
			itemHolder.MouseEnterBounds -= new EventHandler(itemToAdd_MouseEnterBounds);
			itemHolder.MouseLeaveBounds -= new EventHandler(itemToAdd_MouseLeaveBounds);
			itemHolder.ParentChanged -= new EventHandler(itemHolder_ParentChanged);
		}

		private void itemToAdd_MouseEnterBounds(object sender, EventArgs e)
		{
			GuiWidget widgetEntered = ((GuiWidget)sender);
			for (int index = 0; index < topToBottomItemList.Children.Count; index++)
			{
				GuiWidget child = topToBottomItemList.Children[index];
				if (child == widgetEntered)
				{
					HoverIndex = index;
				}
			}
		}

		private void itemToAdd_MouseLeaveBounds(object sender, EventArgs e)
		{
			GuiWidget widgetLeft = ((GuiWidget)sender);

			if (SelectedIndex >= 0)
			{
				if (widgetLeft != topToBottomItemList.Children[SelectedIndex])
				{
					widgetLeft.BackgroundColor = new RGBA_Bytes();
					widgetLeft.Invalidate();
					Invalidate();
				}
			}
		}

		private void LibraryDataReloaded(object sender, EventArgs e)
		{
			UiThread.RunOnIdle(AddAllItems);
		}
	}
}